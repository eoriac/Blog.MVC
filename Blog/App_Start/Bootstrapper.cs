﻿using Autofac;
using Autofac.Core;
using Autofac.Core.Registration;
using Autofac.Core.Resolving.Pipeline;
using Autofac.Integration.Mvc;
using Autofac.Integration.WebApi;
using AutoMapper;
using Blog.Data;
using Blog.Data.Infrastructure;
using Blog.Data.Repositories;
using Blog.Entities;
using Blog.Service;
using log4net;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using Owin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;

namespace Blog.App_Start
{
    public static class Bootstrapper
    {
        public static void Run()
        {
            SetAutofacContainer();
        }

        private static void SetAutofacContainer()
        {
            log4net.Config.XmlConfigurator.Configure();

            var builder = new ContainerBuilder();

            // Registra los controladores
            builder.RegisterControllers(Assembly.GetExecutingAssembly()).ConfigurePipeline(p =>
            {
                p.Use(new Log4NetMiddleware());
            });

            RegisterServices(builder);


            //builder.Register(c => LogManager.GetLogger(typeof(Object))).As<ILog>();

            // Crea el contenedor Autofac
            IContainer container = builder.Build();

            // Configura la resolución de dependencias para los controladores de MVC
            DependencyResolver.SetResolver(new AutofacDependencyResolver(container));
        }

        public static void SetWebApiContainer(this IAppBuilder app)
        {
            var builder = new ContainerBuilder();
            builder.RegisterApiControllers(Assembly.GetExecutingAssembly()).ConfigurePipeline(p =>
            {
                p.Use(new Log4NetMiddleware());
            });

            RegisterServices(builder);

            var container = builder.Build();

            var config = new HttpConfiguration();
            WebApiConfig.Register(config);

            var dependencyResolver = new AutofacWebApiDependencyResolver(container);
            config.DependencyResolver = dependencyResolver;

            app.UseAutofacMiddleware(container);
            app.UseAutofacWebApi(config);
            app.UseWebApi(config);
        }

        private static void RegisterServices(ContainerBuilder builder)
        {
            builder.RegisterType<UnitOfWork>().As<IUnitOfWork>().InstancePerRequest();

            builder.RegisterType<DbFactory>().As<IDbFactory>().InstancePerRequest();

            builder.RegisterAssemblyTypes(Assembly.Load("Blog.Data"))
                .Where(t => t.Name.EndsWith("Repository"))
                .AsImplementedInterfaces().InstancePerLifetimeScope();

            builder.RegisterAssemblyTypes(Assembly.Load("Blog.Service"))
                .Where(t => t.Name.EndsWith("Service"))
                .AsImplementedInterfaces().InstancePerLifetimeScope();

            //builder.RegisterType<AuthorRepository>().As<IAuthorRepository>().InstancePerRequest();

            //builder.RegisterType<AuthorService>().As<IAuthorService>().InstancePerRequest();

            //builder.RegisterType<PostRepository>().As<IPostRepository>().InstancePerRequest();

            //builder.RegisterType<PostService>().As<IPostService>().InstancePerRequest();


            // Registrando AutoMapper
            builder.Register<IConfigurationProvider>(ctx => new MapperConfiguration(cfg => cfg.AddMaps(Assembly.GetExecutingAssembly())));
            builder.Register<IMapper>(ctx => new Mapper(ctx.Resolve<IConfigurationProvider>(), ctx.Resolve)).InstancePerDependency();

            builder.Register(c => new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(new BlogEntities())))
                .As<UserManager<ApplicationUser>>().InstancePerLifetimeScope();
        }
    }

    public class Log4NetMiddleware : IResolveMiddleware
    {
        public PipelinePhase Phase => PipelinePhase.ParameterSelection;

        public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
        {
            // Add our parameters.
            context.ChangeParameters(context.Parameters.Union(
                new[]
                {
                      new ResolvedParameter(
                          (p, i) => p.ParameterType == typeof(ILog),
                          (p, i) => LogManager.GetLogger(p.Member.DeclaringType)
                      ),
                }));

            // Continue the resolve.
            next(context);

            // Has an instance been activated?
            if (context.NewInstanceActivated)
            {
                var instanceType = context.Instance.GetType();

                // Get all the injectable properties to set.
                // If you wanted to ensure the properties were only UNSET properties,
                // here's where you'd do it.
                var properties = instanceType
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.PropertyType == typeof(ILog) && p.CanWrite && p.GetIndexParameters().Length == 0);

                // Set the properties located.
                foreach (var propToSet in properties)
                {
                    propToSet.SetValue(context.Instance, LogManager.GetLogger(instanceType), null);
                }
            }
        }
    }

    internal class LoggingModule : Autofac.Module
    {
        private readonly IResolveMiddleware middleware;

        public LoggingModule(IResolveMiddleware middleware)
        {
            this.middleware = middleware;
        }

        protected override void AttachToComponentRegistration(IComponentRegistryBuilder componentRegistry, IComponentRegistration registration)
        {
            registration.PipelineBuilding +=
                (sender, pipeline) =>
                {
                    // Add our middleware to the pipeline.
                    pipeline.Use(middleware);
                };
        }
    }
}