﻿#region license
// Copyright (c) 2007-2009 Mauricio Scheffer
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//      http://www.apache.org/licenses/LICENSE-2.0
//  
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using System;
using System.Reflection;
using Castle.Core.Configuration;
using Castle.MicroKernel.Facilities;
using Castle.MicroKernel.Registration;
using Castle.MicroKernel.SubSystems.Configuration;
using Castle.Windsor;
using MbUnit.Framework;
using Rhino.Mocks;
using SolrNet;
using SolrNet.Impl;

namespace Castle.Facilities.SolrNetIntegration.Tests {
    [TestFixture]
    public class Tests {
        [Test]
        [ExpectedException(typeof(FacilityException))]
        public void NoConfig_throws() {
            var container = new WindsorContainer();
            container.AddFacility<SolrNetFacility>();
        }

        [Test]
        [ExpectedException(typeof(FacilityException))]
        public void InvalidUrl_throws() {
            var configStore = new DefaultConfigurationStore();
            var configuration = new MutableConfiguration("facility");
            configuration.CreateChild("solrURL", "123");
            configStore.AddFacilityConfiguration("solr", configuration);
            var container = new WindsorContainer(configStore);
            container.AddFacility<SolrNetFacility>("solr");
        }

        [Test]
        [ExpectedException(typeof(FacilityException))]
        public void InvalidProtocol_throws() {
            var configStore = new DefaultConfigurationStore();
            var configuration = new MutableConfiguration("facility");
            configuration.CreateChild("solrURL", "ftp://localhost");
            configStore.AddFacilityConfiguration("solr", configuration);
            var container = new WindsorContainer(configStore);
            container.AddFacility<SolrNetFacility>("solr");
        }

        [Test]
        [Ignore("Requires a running solr instance")]
        public void Ping_Query() {
            var configStore = new DefaultConfigurationStore();
            var configuration = new MutableConfiguration("facility");
            configuration.CreateChild("solrURL", "http://localhost:8983/solr");
            configStore.AddFacilityConfiguration("solr", configuration);
            var container = new WindsorContainer(configStore);
            container.AddFacility<SolrNetFacility>("solr");

            var solr = container.Resolve<ISolrOperations<Document>>();
            solr.Ping();
            Console.WriteLine(solr.Query(SolrQuery.All).Count);
        }

        [Test]
        public void ReplacingMapper() {
            var mapper = MockRepository.GenerateMock<IReadOnlyMappingManager>();
            var solrFacility = new SolrNetFacility("http://localhost:8983/solr") {Mapper = mapper};
            var container = new WindsorContainer();
            container.AddFacility("solr", solrFacility);
            var m = container.Resolve<IReadOnlyMappingManager>();
            Assert.AreSame(m, mapper);
        }

        [Test]
        public void Container_has_ISolrFieldParser() {
            var solrFacility = new SolrNetFacility("http://localhost:8983/solr");
            var container = new WindsorContainer();
            container.AddFacility("solr", solrFacility);
            container.Resolve<ISolrFieldParser>();
        }

        [Test]
        public void Container_has_ISolrFieldSerializer() {
            var solrFacility = new SolrNetFacility("http://localhost:8983/solr");
            var container = new WindsorContainer();
            container.AddFacility("solr", solrFacility);
            container.Resolve<ISolrFieldSerializer>();
        }

        [Test]
        public void Container_has_ISolrDocumentPropertyVisitor() {
            var solrFacility = new SolrNetFacility("http://localhost:8983/solr");
            var container = new WindsorContainer();
            container.AddFacility("solr", solrFacility);
            container.Resolve<ISolrDocumentPropertyVisitor>();
        }

        [Test]
        public void Resolve_ISolrOperations() {
            var solrFacility = new SolrNetFacility("http://localhost:8983/solr");
            var container = new WindsorContainer();
            container.AddFacility("solr", solrFacility);
            container.Resolve<ISolrOperations<Document>>();
        }

        [Test]
        public void ResponseParsers() {
            var solrFacility = new SolrNetFacility("http://localhost:8983/solr");
            var container = new WindsorContainer();
            container.AddFacility("solr", solrFacility);
            var parser = container.Resolve<ISolrQueryResultParser<Document>>() as SolrQueryResultParser<Document>;
            var field = parser.GetType().GetField("parsers", BindingFlags.NonPublic | BindingFlags.Instance);
            var parsers = (ISolrResponseParser<Document>[]) field.GetValue(parser);
            Assert.AreEqual(8, parsers.Length);
            foreach (var t in parsers)
                Console.WriteLine(t);            
        }

        [Test]
        public void MultiCore() {
            const string core0url = "http://localhost:8983/solr/core0";
            const string core1url = "http://localhost:8983/solr/core1";
            var solrFacility = new SolrNetFacility(core0url);
            var container = new WindsorContainer();
            container.AddFacility("solr", solrFacility);

            // override core1 components
            const string core1Connection = "core1.connection";
            container.Register(Component.For<ISolrConnection>().ImplementedBy<SolrConnection>().Named(core1Connection)
                                   .Parameters(Parameter.ForKey("serverURL").Eq(core1url)));
            container.Register(Component.For(typeof (ISolrBasicOperations<Core1Entity>), typeof (ISolrBasicReadOnlyOperations<Core1Entity>))
                                   .ImplementedBy<SolrBasicServer<Core1Entity>>()
                                   .ServiceOverrides(ServiceOverride.ForKey("connection").Eq(core1Connection)));
            container.Register(Component.For<ISolrQueryExecuter<Core1Entity>>().ImplementedBy<SolrQueryExecuter<Core1Entity>>()
                                   .ServiceOverrides(ServiceOverride.ForKey("connection").Eq(core1Connection)));

            // assert that everything is correctly wired
            container.Kernel.DependencyResolving += (client, model, dep) => {
                if (model.TargetType == typeof(ISolrConnection)) {
                    if (client.Service == typeof(ISolrBasicOperations<Core1Entity>) || client.Service == typeof(ISolrQueryExecuter<Core1Entity>))
                        Assert.AreEqual(core1url, ((ISolrConnection) dep).ServerURL);
                    if (client.Service == typeof(ISolrBasicOperations<Document>) || client.Service == typeof(ISolrQueryExecuter<Document>))
                        Assert.AreEqual(core0url, ((ISolrConnection) dep).ServerURL);
                }
            };

            container.Resolve<ISolrOperations<Core1Entity>>();
            container.Resolve<ISolrOperations<Document>>();
        }


        public class Document {}

        public class Core1Entity {}
    }
}