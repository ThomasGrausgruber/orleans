using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Orleans;
using Orleans.Providers;
using Orleans.Runtime.Configuration;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.TestingHost;
using Tester.TestStreamProviders;
using Orleans.Providers.Streams.Generator;
using Orleans.TestingHost.Utils;
using TestGrainInterfaces;
using TestGrains;
using Tester;
using UnitTests.Grains;
using UnitTests.Tester;

namespace UnitTests.StreamingTests
{
    // if we paralellize tests, this should run in isolation 
    public class DynamicStreamProviderConfigurationTests : OrleansTestingBase, IClassFixture<DynamicStreamProviderConfigurationTests.Fixture>, IDisposable
    {
        private Fixture fixture;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
        private IManagementGrain mgmtGrain;
        private const string streamProviderName1 = "GeneratedStreamProvider1";
        private const string streamProviderName2 = "GeneratedStreamProvider2";

        public class Fixture : BaseTestClusterFixture
        {
            public const string StreamNamespace = GeneratedEventCollectorGrain.StreamNamespace;
            public Dictionary<string, string> DefaultStreamProviderSettings = new Dictionary<string, string>();

            public readonly static SimpleGeneratorConfig GeneratorConfig = new SimpleGeneratorConfig
            {
                StreamNamespace = StreamNamespace,
                EventsInStream = 100
            };

            public readonly static GeneratorAdapterConfig AdapterConfig = new GeneratorAdapterConfig(GeneratedStreamTestConstants.StreamProviderName)
            {
                TotalQueueCount = 4,
                GeneratorConfigType = GeneratorConfig.GetType()
            };

            protected override TestCluster CreateTestCluster()
            {
                var options = new TestClusterOptions(1);
                // get initial settings from configs
                AdapterConfig.WriteProperties(DefaultStreamProviderSettings);
                GeneratorConfig.WriteProperties(DefaultStreamProviderSettings);

                // add queue balancer setting
                DefaultStreamProviderSettings.Add(PersistentStreamProviderConfig.QUEUE_BALANCER_TYPE, StreamQueueBalancerType.DynamicClusterConfigDeploymentBalancer.ToString());

                // add pub/sub settting
                DefaultStreamProviderSettings.Add(PersistentStreamProviderConfig.STREAM_PUBSUB_TYPE, StreamPubSubType.ImplicitOnly.ToString());

                return new TestCluster(options);
            }
        }

        public async void Dispose()
        {
            await RemoveAllProviders();
        }

        public DynamicStreamProviderConfigurationTests(Fixture fixture)
        {
            this.fixture = fixture;
            RemoveAllProviders().WaitWithThrow(Timeout);
        }

        [Fact, TestCategory("Functional"), TestCategory("Streaming"), TestCategory("Providers")]
        public async Task DynamicStreamProviderConfigurationTests_AddAndRemoveProvidersAndCheckCounters()
        {
            //Making sure the initial provider list is empty.
            IEnumerable<String> providerNames = fixture.HostedCluster.Primary.Silo.TestHook.GetStreamProviderNames();
            Assert.Equal(providerNames.Count(), 0);

            providerNames = new [] { GeneratedStreamTestConstants.StreamProviderName };
            var reporter = GrainClient.GrainFactory.GetGrain<IGeneratedEventReporterGrain>(GeneratedStreamTestConstants.ReporterId);
            try
            {
                await AddGeneratorStreamProviderAndVerify(providerNames);
                await TestingUtils.WaitUntilAsync(CheckCounters, Timeout);

                await reporter.Reset();
                await RemoveProvidersAndVerify(providerNames);
                await AddGeneratorStreamProviderAndVerify(providerNames);
                await TestingUtils.WaitUntilAsync(CheckCounters, Timeout);
            }
            finally
            {
                await reporter.Reset();
            }
        }

        [Fact, TestCategory("Functional"), TestCategory("Streaming"), TestCategory("Providers")]
        public async Task DynamicStreamProviderConfigurationTests_AddAndRemoveProvidersInBatch()
        {
            //Making sure the initial provider list is empty.
            IEnumerable<String> providerNames = fixture.HostedCluster.Primary.Silo.TestHook.GetStreamProviderNames();
            Assert.Equal(providerNames.Count(), 0);

            providerNames = new[]
            {
                GeneratedStreamTestConstants.StreamProviderName,
                streamProviderName1,
                streamProviderName2
            };
            await AddGeneratorStreamProviderAndVerify(providerNames);
            await RemoveProvidersAndVerify(providerNames);
            providerNames = new[]
            {
                streamProviderName1,
                streamProviderName2
            };
            await AddGeneratorStreamProviderAndVerify(providerNames);
        }

        [Fact, TestCategory("Functional"), TestCategory("Streaming"), TestCategory("Providers")]
        public async Task DynamicStreamProviderConfigurationTests_AddAndRemoveProvidersIndividually()
        { 
            //Making sure the initial provider list is empty.
            IEnumerable<String> providerNames = fixture.HostedCluster.Primary.Silo.TestHook.GetStreamProviderNames();
            Assert.Equal(providerNames.Count(), 0);

            providerNames = new[] { GeneratedStreamTestConstants.StreamProviderName };
            await AddGeneratorStreamProviderAndVerify(providerNames);

            providerNames = new [] { streamProviderName1 };
            await AddGeneratorStreamProviderAndVerify(providerNames);

            providerNames = new [] { streamProviderName2 };
            await AddGeneratorStreamProviderAndVerify(providerNames);

            providerNames = new[] { streamProviderName2 };
            await RemoveProvidersAndVerify(providerNames);

            providerNames = new[] { streamProviderName2 };
            await AddGeneratorStreamProviderAndVerify(providerNames);

            providerNames = new[] { streamProviderName1 };
            await RemoveProvidersAndVerify(providerNames);

            providerNames = new[] { GeneratedStreamTestConstants.StreamProviderName };
            await RemoveProvidersAndVerify(providerNames);

            providerNames = new[] { GeneratedStreamTestConstants.StreamProviderName };
            await AddGeneratorStreamProviderAndVerify(providerNames);

            providerNames = new[] { streamProviderName1 };
            await AddGeneratorStreamProviderAndVerify(providerNames);

            providerNames = new[] { streamProviderName1 };
            await RemoveProvidersAndVerify(providerNames);

            providerNames = new[] { GeneratedStreamTestConstants.StreamProviderName };
            await RemoveProvidersAndVerify(providerNames);

        }

        [Fact, TestCategory("Functional"), TestCategory("Streaming"), TestCategory("Providers")]
        public async Task DynamicStreamProviderConfigurationTests_AddProvidersAndThrowExceptionOnInitialization()
        {
            //Making sure the initial provider list is empty.
            IEnumerable<String> providerNames = fixture.HostedCluster.Primary.Silo.TestHook.GetStreamProviderNames();
            Assert.Equal(providerNames.Count(), 0);

            bool exceptionThrown = false;
            Dictionary<string, string> providerSettings = new Dictionary<string, string>(fixture.DefaultStreamProviderSettings);
            providerSettings.Add(FailureInjectionStreamProvider.FailureInjectionModeString, 
                FailureInjectionStreamProviderMode.InitializationThrowsException.ToString());
            providerNames = new [] {"FailureInjectionStreamProvider"};
            try
            {
                await AddFailureInjectionStreamProviderAndVerify(providerNames, providerSettings);
            }
            catch (ProviderInitializationException)
            {
                exceptionThrown = true;
            }
            Assert.Equal(exceptionThrown, true);
        }

        [Fact, TestCategory("Functional"), TestCategory("Streaming"), TestCategory("Providers")]
        public async Task DynamicStreamProviderConfigurationTests_AddProvidersAndThrowExceptionOnStart()
        {
            //Making sure the initial provider list is empty.
            IEnumerable<String> providerNames = fixture.HostedCluster.Primary.Silo.TestHook.GetStreamProviderNames();
            Assert.Equal(providerNames.Count(), 0);
            bool exceptionThrown = false;
            Dictionary<string, string> providerSettings = new Dictionary<string, string>(fixture.DefaultStreamProviderSettings);
            providerSettings.Add(FailureInjectionStreamProvider.FailureInjectionModeString,
                FailureInjectionStreamProviderMode.StartThrowsException.ToString());
            providerNames = new [] { "FailureInjectionStreamProvider"};
            try
            {
                await AddFailureInjectionStreamProviderAndVerify(providerNames, providerSettings);
            }
            catch (ProviderStartException)
            {
                exceptionThrown = true;
            }
            Assert.Equal(exceptionThrown, true);
        }

        private async Task RemoveAllProviders()
        {
            IEnumerable<String> ProviderNames = fixture.HostedCluster.Primary.Silo.TestHook.GetStreamProviderNames();
            await RemoveProvidersAndVerify(ProviderNames);
            ProviderNames = fixture.HostedCluster.Primary.Silo.TestHook.GetStreamProviderNames();
            Assert.Equal(ProviderNames.Count(), 0);
        }

        private async Task AddFailureInjectionStreamProviderAndVerify(IEnumerable<string> streamProviderNames, Dictionary<string, string> ProviderSettings)
        {
            foreach (String providerName in streamProviderNames)
            {
                fixture.HostedCluster.ClusterConfiguration.Globals.RegisterStreamProvider<FailureInjectionStreamProvider>(providerName, ProviderSettings);
            }
            await AddProvidersAndVerify(streamProviderNames);
        }

        private async Task AddGeneratorStreamProviderAndVerify(IEnumerable<string> streamProviderNames)
        {
            foreach (String providerName in streamProviderNames)
            {
                fixture.HostedCluster.ClusterConfiguration.Globals.RegisterStreamProvider<GeneratorStreamProvider>(providerName, fixture.DefaultStreamProviderSettings);
            }
            await AddProvidersAndVerify(streamProviderNames);
        }

        private async Task AddProvidersAndVerify(IEnumerable<string> streamProviderNames)
        {
            mgmtGrain = GrainClient.GrainFactory.GetGrain<IManagementGrain>(RuntimeInterfaceConstants.SYSTEM_MANAGEMENT_ID);
            IEnumerable<String> names = fixture.HostedCluster.Primary.Silo.TestHook.GetStreamProviderNames();

            IDictionary<String, bool> hasNewProvider = new Dictionary<String, bool>();

            int Count = names.Count();
            SiloAddress[] address = new SiloAddress[1];
            address[0] = fixture.HostedCluster.Primary.Silo.SiloAddress;
            await mgmtGrain.UpdateStreamProviders(address, fixture.HostedCluster.ClusterConfiguration.Globals.ProviderConfigurations);
            names = fixture.HostedCluster.Primary.Silo.TestHook.GetStreamProviderNames();
            IEnumerable<String> allSiloProviderNames = fixture.HostedCluster.Primary.Silo.TestHook.GetAllSiloProviderNames();
            Assert.Equal(names.Count() - Count, streamProviderNames.Count());
            Assert.Equal(allSiloProviderNames.Count(), names.Count());
            foreach (String name in names)
            {
                if (streamProviderNames.Contains(name))
                {
                    Assert.Equal(hasNewProvider.ContainsKey(name), false);
                    hasNewProvider[name] = true;
                }
            }

            Assert.Equal(hasNewProvider.Count, streamProviderNames.Count());

            hasNewProvider.Clear();
            foreach (String name in allSiloProviderNames)
            {
                if (streamProviderNames.Contains(name))
                {
                    Assert.Equal(hasNewProvider.ContainsKey(name), false);
                    hasNewProvider[name] = true;
                }
            }
            Assert.Equal(hasNewProvider.Count, streamProviderNames.Count());
        }

        private async Task RemoveProvidersAndVerify(IEnumerable<String> streamProviderNames)
        {
            mgmtGrain = GrainClient.GrainFactory.GetGrain<IManagementGrain>(RuntimeInterfaceConstants.SYSTEM_MANAGEMENT_ID);
            IEnumerable<String> names = fixture.HostedCluster.Primary.Silo.TestHook.GetStreamProviderNames();
            int Count = names.Count();
            IDictionary<String, bool> foundProvider = new Dictionary<string, bool>();
            foreach (String name in streamProviderNames)
            {
                if(fixture.HostedCluster.ClusterConfiguration.Globals.ProviderConfigurations[ProviderCategoryConfiguration.STREAM_PROVIDER_CATEGORY_NAME].Providers.ContainsKey(name))
                    fixture.HostedCluster.ClusterConfiguration.Globals.ProviderConfigurations[ProviderCategoryConfiguration.STREAM_PROVIDER_CATEGORY_NAME].Providers.Remove(name);
            }

            SiloAddress[] address = new SiloAddress[1];
            address[0] = fixture.HostedCluster.Primary.Silo.SiloAddress;
            await mgmtGrain.UpdateStreamProviders(address, fixture.HostedCluster.ClusterConfiguration.Globals.ProviderConfigurations);
            names = fixture.HostedCluster.Primary.Silo.TestHook.GetStreamProviderNames();
            IEnumerable<String> allSiloProviderNames = fixture.HostedCluster.Primary.Silo.TestHook.GetAllSiloProviderNames();
            Assert.Equal(Count - names.Count(), streamProviderNames.Count());
            Assert.Equal(allSiloProviderNames.Count(), names.Count());
            foreach (String name in streamProviderNames)
            {
                Assert.Equal(names.Contains(name), false);
            }
            foreach (String name in streamProviderNames)
            {
                Assert.Equal(allSiloProviderNames.Contains(name), false);
            }
        }

        private async Task<bool> CheckCounters(bool assertIsTrue)
        {
            var reporter = GrainClient.GrainFactory.GetGrain<IGeneratedEventReporterGrain>(GeneratedStreamTestConstants.ReporterId);

            var report = await reporter.GetReport(GeneratedStreamTestConstants.StreamProviderName, Fixture.StreamNamespace);
            if (assertIsTrue)
            {
                // one stream per queue
                Assert.Equal(Fixture.AdapterConfig.TotalQueueCount, report.Count);
                foreach (int eventsPerStream in report.Values)
                {
                    Assert.Equal(Fixture.GeneratorConfig.EventsInStream, eventsPerStream);
                }
            }
            else if (Fixture.AdapterConfig.TotalQueueCount != report.Count ||
                     report.Values.Any(count => count != Fixture.GeneratorConfig.EventsInStream))
            {
                return false;
            }
            return true;
        }
    }
}
