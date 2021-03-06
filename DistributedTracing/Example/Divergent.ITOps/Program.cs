﻿using System;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using Divergent.ITOps.Interfaces;
using ITOps.EndpointConfig;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NServiceBus;
using NServiceBus.Extensions.Diagnostics.OpenTelemetry;
using OpenTelemetry.Trace;

namespace Divergent.ITOps
{
    public class Program
    {
        public static string EndpointName => "Divergent.ITOps";

        public static void Main(string[] args)
        {
            Activity.DefaultIdFormat = ActivityIdFormat.W3C;
            Activity.ForceDefaultIdFormat = true;

            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((builder, services) =>
                {
                    var assemblies = ReflectionHelper.GetAssemblies("..\\..\\..\\Providers", ".Data.dll");
                    services.Scan(s =>
                    {
                        s.FromAssemblies(assemblies)
                            .AddClasses(classes => classes.Where(t => t.Name.EndsWith("Provider")))
                            .AsImplementedInterfaces()
                            .WithTransientLifetime();
                    });

                    var serviceRegistrars = assemblies
                        .SelectMany(a => a.GetTypes())
                        .Where(t => typeof(IRegisterServices).IsAssignableFrom(t))
                        .Select(Activator.CreateInstance)
                        .Cast<IRegisterServices>()
                        .ToList();

                    foreach (var serviceRegistrar in serviceRegistrars)
                    {
                        serviceRegistrar.Register(builder, services);
                    }

                    services.AddOpenTelemetryTracing(config => config
                        .AddZipkinExporter(o =>
                        {
                            o.Endpoint = new Uri("http://localhost:9411/api/v2/spans");
                            o.ServiceName = EndpointName;
                        })
                        .AddJaegerExporter(c =>
                        {
                            c.AgentHost = "localhost";
                            c.AgentPort = 6831;
                            c.ServiceName = EndpointName;
                        })
                        .AddNServiceBusInstrumentation(opt => opt.CaptureMessageBody = true)
                        .AddSqlClientInstrumentation(opt => opt.SetTextCommandContent = true)
                    );
                })
                .UseNServiceBus(context =>
                {
                    var endpoint = new EndpointConfiguration(EndpointName);
                    endpoint.Configure();

                    return endpoint;
                });
    }
}
