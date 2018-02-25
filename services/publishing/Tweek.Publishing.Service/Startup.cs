﻿using System;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minio;
using Newtonsoft.Json;
using Polly;
using Tweek.Publishing.Service.Handlers;
using Tweek.Publishing.Service.Messaging;
using Tweek.Publishing.Service.Packing;
using Tweek.Publishing.Service.Storage;
using Tweek.Publishing.Service.Sync;
using Tweek.Publishing.Service.Sync.Uploaders;
using Tweek.Publishing.Service.Utils;
using Tweek.Publishing.Service.Validation;

namespace Tweek.Publishing.Service
{
    public class Startup
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger _logger;

        public Startup(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _configuration = configuration;
            _logger = loggerFactory.CreateLogger("Default");
        }

        private void RunSSHDeamon(IApplicationLifetime lifetime, ILogger logger)
        {
            var sshdConfigLocation = _configuration.GetValue<string>("SSHD_CONFIG_LOCATION");
            var job = ShellHelper.Executor.ExecObservable("/usr/sbin/sshd", $"-f {sshdConfigLocation}")
                .Retry(3)
                .Select(x => (data: Encoding.Default.GetString(x.data), x.outputType))
                .Subscribe(x =>
                {
                    if (x.outputType == OutputType.StdOut)
                    {
                        logger.LogInformation(x.data);
                    }
                    if (x.outputType == OutputType.StdErr)
                    {
                        logger.LogWarning(x.data);
                    }
                }, ex =>
                {
                    logger.LogError("error:" + ex.ToString());
                    lifetime.StopApplication();
                });
            lifetime.ApplicationStopping.Register(job.Dispose);
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRouting();
        }

        private MinioClient CreateMinioClient(IConfiguration minioConfig)
        {
            var mc = new MinioClient(
                endpoint: minioConfig.GetValue<string>("Endpoint"),
                accessKey: minioConfig.GetValueInlineOrFile("AccessKey"),
                secretKey: minioConfig.GetValueInlineOrFile("SecretKey")
            );
            return minioConfig.GetValue("UseSSL", false) ? mc.WithSSL() : mc;
        }

        private StorageSynchronizer CreateStorageSynchronizer(IObjectStorage storageClient, ShellHelper.ShellExecutor executor)
        {
            var storageSynchronizer = new StorageSynchronizer(storageClient, executor)
            {
                Uploaders =
                {
                    new RulesUploader(storageClient, executor, new Packer()),
                    new PolicyUploader(storageClient, executor),
                }
            };
            return storageSynchronizer;
        }

        private void RunIntervalPublisher(IApplicationLifetime lifetime, Func<string,Task> publisher,
            RepoSynchronizer repoSynchronizer, StorageSynchronizer storageSynchronizer)
        {
            var intervalPublisher = new IntervalPublisher(publisher);
            var job = intervalPublisher.PublishEvery(TimeSpan.FromSeconds(60), async () =>
            {
                var commitId = await repoSynchronizer.CurrentHead();
                await Policy.Handle<StaleRevisionException>()
                    .RetryAsync(10, async (_,c)=> await repoSynchronizer.SyncToLatest())
                    .ExecuteAsync(async ()=> await storageSynchronizer.Sync(commitId));

                _logger.LogInformation($"SyncVersion:{commitId}");
                return commitId;
            });
            lifetime.ApplicationStopping.Register(job.Dispose);
        }

        private const string DEFAULT_MINIO_BUCKET_NAME = @"tweek";

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory,
            IApplicationLifetime lifetime)
        {
            _logger.LogInformation("Starting service");
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            RunSSHDeamon(lifetime, _logger);

            var executor = ShellHelper.Executor.WithWorkingDirectory(_configuration.GetValue<string>("REPO_LOCATION"))
                                               .ForwardEnvVariable("GIT_SSH");
            var git = executor.CreateCommandExecutor("git");

            var gitValidationFlow = new GitValidationFlow
            {
                Validators =
                {
                    (Patterns.Manifests, new CircularDependencyValidator()),
                    (Patterns.JPad, new CompileJPadValidator())
                }
            };

            var minioConfig = _configuration.GetSection("Minio");
            
            var storageClient = Policy.Handle<Exception>()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
                .ExecuteAsync(() =>
                    MinioBucketStorage.GetOrCreateBucket(CreateMinioClient(minioConfig),
                        minioConfig.GetValue("Bucket", DEFAULT_MINIO_BUCKET_NAME)))
                .Result;

            var natsClient = new NatsPublisher(_configuration.GetSection("Nats").GetValue<string>("Endpoint"));
            var versionPublisher = natsClient.GetSubjectPublisher("version");
            var repoSynchronizer = new RepoSynchronizer(git);
            var storageSynchronizer = CreateStorageSynchronizer(storageClient, executor);

            storageSynchronizer.Sync(repoSynchronizer.CurrentHead().Result, checkForStaleRevision: false).Wait();
            RunIntervalPublisher(lifetime, versionPublisher, repoSynchronizer, storageSynchronizer);
            
            app.UseRouter(router =>
            {
                router.MapGet("validate", ValidationHandler.Create(executor, gitValidationFlow));
                router.MapGet("sync", SyncHandler.Create(storageSynchronizer, repoSynchronizer, versionPublisher,
                Policy.Handle<Exception>()
                        .WaitAndRetryAsync(3,
                            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                            async (ex, timespan) =>
                            {
                                _logger.LogWarning("Sync:Retrying");
                                await Task.Delay(timespan);
                            }),
                 _logger));
                router.MapGet("push-failed", async (req, res, routedata) => await natsClient.Publish("push-failed", req.Query["commit"] ));

                router.MapGet("log", async (req, res, routedata) => _logger.LogInformation(req.Query["message"]));
                router.MapGet("health", async (req, res, routedata) => await res.WriteAsync(JsonConvert.SerializeObject(new { })));
                router.MapGet("version", async (req, res, routedata) => await res.WriteAsync(Assembly.GetEntryAssembly()
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion));
            });
        }
    }
}