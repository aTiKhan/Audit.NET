﻿using Audit.Core;
using System;
using Moq;
using Audit.Core.Providers;
using Audit.EntityFramework;
using System.Collections.Generic;
using Audit.Core.Extensions;
using Audit.Core.ConfigurationApi;
using System.Diagnostics;
using NUnit.Framework;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Audit.EntityFramework.ConfigurationApi;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Audit.UnitTest
{
    public class UnitTest
    {
        [SetUp]
        public void Setup()
        {
            Audit.Core.Configuration.AuditDisabled = false;
            Audit.Core.Configuration.ResetCustomActions();
        }

        [Test]
        public void Test_AuditScope_CustomSystemClock()
        {
            Audit.Core.Configuration.SystemClock = new MyClock();
            var evs = new List<AuditEvent>();
            Audit.Core.Configuration.Setup()
                .Use(x => x
                    .OnInsertAndReplace(ev =>
                    {
                        evs.Add(ev);
                    }))
                .WithCreationPolicy(EventCreationPolicy.InsertOnEnd);

            using (var scope = AuditScope.Create("Test_AuditScope_CustomSystemClock", () => new { someProp = true }))
            {
                scope.SetCustomField("test", 123);
            }
            Audit.Core.Configuration.SystemClock = new DefaultSystemClock();

            Assert.AreEqual(1, evs.Count);
            Assert.AreEqual(10000, evs[0].Duration);
            Assert.AreEqual(new DateTime(2020, 1, 1, 0, 0, 0), evs[0].StartDate);
            Assert.AreEqual(new DateTime(2020, 1, 1, 0, 0, 10), evs[0].EndDate);
        }

        [Test]
        public void Test_AuditScope_SetTargetGetter()
        {
            var evs = new List<AuditEvent>();
            Audit.Core.Configuration.Setup()
                .Use(x => x
                    .OnInsertAndReplace(ev =>
                    {
                        evs.Add(ev);
                    }))
                .WithCreationPolicy(EventCreationPolicy.InsertOnEnd);

            var obj = new SomeClass() { Id = 1, Name = "Test" };

            using (var scope = AuditScope.Create("Test", () => new { ShouldNotUseThisObject = true }))
            {
                scope.SetTargetGetter(() => obj);
                obj.Id = 2;
                obj.Name = "NewTest";
            }
            obj.Id = 3;
            obj.Name = "X";

            Assert.AreEqual(1, evs.Count);
            Assert.AreEqual("SomeClass", evs[0].Target.Type);
            Assert.AreEqual(1, (evs[0].Target.Old as JObject).ToObject<SomeClass>().Id);
            Assert.AreEqual("Test", (evs[0].Target.Old as JObject).ToObject<SomeClass>().Name);
            Assert.AreEqual(2, (evs[0].Target.New as JObject).ToObject<SomeClass>().Id);
            Assert.AreEqual("NewTest", (evs[0].Target.New as JObject).ToObject<SomeClass>().Name);
        }

        [Test]
        public void Test_AuditScope_SetTargetGetter_ReturnsNull()
        {
            var evs = new List<AuditEvent>();
            Audit.Core.Configuration.Setup()
                .Use(x => x
                    .OnInsertAndReplace(ev =>
                    {
                        evs.Add(ev);
                    }))
                .WithCreationPolicy(EventCreationPolicy.InsertOnEnd);


            using (var scope = AuditScope.Create("Test", () => new { ShouldNotUseThisObject = true }))
            {
                scope.SetTargetGetter(() => null);
            }

            Assert.AreEqual(1, evs.Count);
            Assert.AreEqual("Object", evs[0].Target.Type);
            Assert.IsNull(evs[0].Target.Old);
            Assert.IsNull(evs[0].Target.New);
        }

        [Test]
        public void Test_AuditScope_SetTargetGetter_IsNull()
        {
            var evs = new List<AuditEvent>();
            Audit.Core.Configuration.Setup()
                .Use(x => x
                    .OnInsertAndReplace(ev =>
                    {
                        evs.Add(ev);
                    }))
                .WithCreationPolicy(EventCreationPolicy.InsertOnEnd);


            using (var scope = AuditScope.Create("Test", () => new { ShouldNotUseThisObject = true }))
            {
                scope.SetTargetGetter(null);
            }

            Assert.AreEqual(1, evs.Count);
            Assert.IsNull(evs[0].Target);
        }

        [Test]
        public void Test_AuditEvent_CustomSerializer()
        {
            var listEv = new List<AuditEvent>();
            var listJson = new List<string>();
            Audit.Core.Configuration.Setup()
                .Use(x => x
                    .OnInsertAndReplace(ev =>
                    {
                        listEv.Add(ev);
                        listJson.Add(ev.ToJson());
                    }))
                .WithCreationPolicy(EventCreationPolicy.InsertOnEnd);

            var jSettings = new JsonSerializerSettings()
            {
               TypeNameHandling = TypeNameHandling.All
            };

            Audit.Core.Configuration.JsonSettings = jSettings;
            using (var scope = AuditScope.Create("TEST", null, null))
            {
            }
            
            Assert.AreEqual(1, listEv.Count);

            var manualJson = listEv[0].ToJson();

            Audit.Core.Configuration.JsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };


            Assert.AreEqual(1, listJson.Count);

            var jsonExpected = JsonConvert.SerializeObject(listEv[0], jSettings);
            Assert.AreEqual(jsonExpected, listJson[0]);
            Assert.AreEqual(jsonExpected, manualJson);
            Assert.AreEqual(JsonConvert.SerializeObject(listEv[0], new JsonSerializerSettings()), listEv[0].ToJson(new JsonSerializerSettings()));
        }

        [Test]
        public void Test_AuditDisable_AllDisabled()
        {
            var list = new List<AuditEvent>();
            Audit.Core.Configuration.Setup()
                .AuditDisabled(true)
                .Use(x => x
                    .OnInsertAndReplace(ev =>
                    {
                        list.Add(ev);
                    }))
                .WithCreationPolicy(EventCreationPolicy.InsertOnStartReplaceOnEnd);

            using (var scope = AuditScope.Create("", null, null))
            {
                scope.Save();
                scope.SaveAsync().Wait();
            }
            Audit.Core.Configuration.AuditDisabled = false;
            Assert.AreEqual(0, list.Count);
        }

        [Test]
        public void Test_AuditDisable_OnAction()
        {
            var list = new List<AuditEvent>();
            Audit.Core.Configuration.Setup()
                .UseDynamicProvider(x => x
                    .OnInsertAndReplace(ev =>
                    {
                        list.Add(ev);
                    }))
                .WithCreationPolicy(EventCreationPolicy.InsertOnStartReplaceOnEnd)
                .WithAction(_ => _.OnEventSaving(scope =>
                {
                    Audit.Core.Configuration.AuditDisabled = true;
                }));

            using (var scope = AuditScope.Create("", null, null))
            {
                scope.Save();
                scope.SaveAsync().Wait();
            }
            Audit.Core.Configuration.AuditDisabled = false;
            Assert.AreEqual(0, list.Count);
        }

        [Test]
        public void Test_EF_MergeEntitySettings()
        {
            var now = DateTime.Now;
            var helper = new DbContextHelper();
            var attr = new Dictionary<Type, EfEntitySettings>();
            var local = new Dictionary<Type, EfEntitySettings>();
            var global = new Dictionary<Type, EfEntitySettings>();
            attr[typeof(string)] = new EfEntitySettings()
            {
                IgnoredProperties = new HashSet<string>(new[] { "I1" }),
                OverrideProperties = new Dictionary<string, object>() { { "C1", 1 }, { "C2", "ATTR" } }
            };
            local[typeof(string)] = new EfEntitySettings()
            {
                IgnoredProperties = new HashSet<string>(new[] { "I1", "I2" }),
                OverrideProperties = new Dictionary<string, object>() { { "C2", "LOCAL" }, { "C3", now } } 
            };
            global[typeof(string)] = new EfEntitySettings()
            {
                IgnoredProperties = new HashSet<string>(new[] { "I3" }),
                OverrideProperties = new Dictionary<string, object>() { { "C2", "GLOBAL" }, { "C4", null } }
            };

            attr[typeof(int)] = new EfEntitySettings()
            {
                IgnoredProperties = new HashSet<string>(new[] { "I3" }),
                OverrideProperties = new Dictionary<string, object>() { { "C2", "INT" }, { "C4", null } }
            };

            var merged = helper.MergeEntitySettings(attr, local, global);
            var mustbenull1 = helper.MergeEntitySettings(null, null, null);
            var mustbenull2 = helper.MergeEntitySettings(null, new Dictionary<Type, EfEntitySettings>(), null);
            var mustbenull3 = helper.MergeEntitySettings(new Dictionary<Type, EfEntitySettings>(), new Dictionary<Type, EfEntitySettings>(), new Dictionary<Type, EfEntitySettings>());
            Assert.AreEqual(2, merged.Count);
            Assert.IsNull(mustbenull1);
            Assert.IsNull(mustbenull2);
            Assert.IsNull(mustbenull3);
            var merge = merged[typeof(string)];
            Assert.AreEqual(3, merge.IgnoredProperties.Count);
            Assert.IsTrue(merge.IgnoredProperties.Contains("I1"));
            Assert.IsTrue(merge.IgnoredProperties.Contains("I2"));
            Assert.IsTrue(merge.IgnoredProperties.Contains("I3"));
            Assert.AreEqual(4, merge.OverrideProperties.Count);
            Assert.AreEqual(1, merge.OverrideProperties["C1"]);
            Assert.AreEqual("ATTR", merge.OverrideProperties["C2"]);
            Assert.AreEqual(now, merge.OverrideProperties["C3"]);
            Assert.AreEqual(null, merge.OverrideProperties["C4"]);
            merge = merged[typeof(int)];
            Assert.AreEqual(1, merge.IgnoredProperties.Count);
            Assert.IsTrue(merge.IgnoredProperties.Contains("I3"));
            Assert.AreEqual("INT", merge.OverrideProperties["C2"]);
            Assert.AreEqual(null, merge.OverrideProperties["C4"]);
        }

        [Test]
        public void Test_FileLog_HappyPath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "Test_FileLog_HappyPath");
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }

            Audit.Core.Configuration.Setup()
                .UseFileLogProvider(x => x
                    .Directory(dir)
                    .FilenameBuilder(_ => $"{_.EventType}-{_.CustomFields["X"]}.json"))
                .WithCreationPolicy(EventCreationPolicy.InsertOnStartReplaceOnEnd);

            var target = "start";
            using (var scope = AuditScope.Create("evt", () => target, new {X = 1}))
            {
                target = "end";
            }
            var fileFromProvider = (Audit.Core.Configuration.DataProvider as FileDataProvider).GetEvent($@"{dir}\evt-1.json");

            var ev = JsonConvert.DeserializeObject<AuditEvent>(File.ReadAllText(Path.Combine(dir, "evt-1.json")));
            var fileCount = Directory.EnumerateFiles(dir).Count();
            Directory.Delete(dir, true);

            Assert.AreEqual(1, fileCount);
            Assert.AreEqual(JsonConvert.SerializeObject(ev), JsonConvert.SerializeObject(fileFromProvider));
            Assert.AreEqual("evt", ev.EventType);
            Assert.AreEqual("start", ev.Target.Old);
            Assert.AreEqual("end", ev.Target.New);
            Assert.AreEqual("1", ev.CustomFields["X"].ToString());
        }

        [Test]
        public void Test_ScopeSaveMode_CreateAndSave()
        {
            var modes = new List<SaveMode>();
            Audit.Core.Configuration.Setup()
                .UseDynamicProvider(x => x
                    .OnInsert(ev => { })
                    .OnReplace((id, ev) => { }))
                .WithCreationPolicy(EventCreationPolicy.Manual)
                .WithAction(a => a
                    .OnEventSaving(scope => 
                    {
                        modes.Add(scope.SaveMode);
                    }));

            using (var scope = AuditScope.Create(new AuditScopeOptions() { IsCreateAndSave = true }))
            {
                scope.Save();
            }

            Assert.AreEqual(1, modes.Count);
            Assert.AreEqual(SaveMode.InsertOnStart, modes[0]);
        }

        [Test]
        public void Test_ScopeSaveMode_InsertOnStartReplaceOnEnd()
        {
            var modes = new List<SaveMode>();
            Audit.Core.Configuration.Setup()
                .UseDynamicProvider(x => x
                    .OnInsert(ev => { })
                    .OnReplace((id, ev) => { }))
                .WithCreationPolicy(EventCreationPolicy.InsertOnStartReplaceOnEnd)
                .WithAction(a => a
                    .OnEventSaving(scope =>
                    {
                        modes.Add(scope.SaveMode);
                    }));

            using (var scope = AuditScope.Create(new AuditScopeOptions() { }))
            {
                scope.Save();
            }

            Assert.AreEqual(3, modes.Count);
            Assert.AreEqual(SaveMode.InsertOnStart, modes[0]);
            Assert.AreEqual(SaveMode.ReplaceOnEnd, modes[1]);
            Assert.AreEqual(SaveMode.ReplaceOnEnd, modes[2]);
        }

        [Test]
        public void Test_ScopeSaveMode_InsertOnStartInsertOnEnd()
        {
            var modes = new List<SaveMode>();
            Audit.Core.Configuration.Setup()
                .UseDynamicProvider(x => x
                    .OnInsert(ev => { })
                    .OnReplace((id, ev) => { }))
                .WithCreationPolicy(EventCreationPolicy.InsertOnStartInsertOnEnd)
                .WithAction(a => a
                    .OnEventSaving(scope =>
                    {
                        modes.Add(scope.SaveMode);
                    }));

            using (var scope = AuditScope.Create(new AuditScopeOptions() { }))
            {
                scope.Save();
            }

            Assert.AreEqual(3, modes.Count);
            Assert.AreEqual(SaveMode.InsertOnStart, modes[0]);
            Assert.AreEqual(SaveMode.InsertOnEnd, modes[1]);
            Assert.AreEqual(SaveMode.InsertOnEnd, modes[2]);
        }

        [Test]
        public void Test_ScopeSaveMode_InsertOnEnd()
        {
            var modes = new List<SaveMode>();
            Audit.Core.Configuration.Setup()
                .UseDynamicProvider(x => x
                    .OnInsert(ev => { })
                    .OnReplace((id, ev) => { }))
                .WithCreationPolicy(EventCreationPolicy.InsertOnEnd)
                .WithAction(a => a
                    .OnEventSaving(scope =>
                    {
                        modes.Add(scope.SaveMode);
                    }));

            using (var scope = AuditScope.Create(new AuditScopeOptions() { }))
            {
                scope.Save();
            }

            Assert.AreEqual(2, modes.Count);
            Assert.AreEqual(SaveMode.InsertOnEnd, modes[0]);
            Assert.AreEqual(SaveMode.InsertOnEnd, modes[1]);
        }

        [Test]
        public void Test_ScopeSaveMode_Manual()
        {
            var modes = new List<SaveMode>();
            Audit.Core.Configuration.Setup()
                .UseDynamicProvider(x => x
                    .OnInsert(ev => { })
                    .OnReplace((id, ev) => { }))
                .WithCreationPolicy(EventCreationPolicy.Manual)
                .WithAction(a => a
                    .OnEventSaving(scope =>
                    {
                        modes.Add(scope.SaveMode);
                    }));

            using (var scope = AuditScope.Create(new AuditScopeOptions() { }))
            {
                scope.Save();
            }

            Assert.AreEqual(1, modes.Count);
            Assert.AreEqual(SaveMode.Manual, modes[0]);
        }

        [Test]
        public void Test_ScopeActionsStress()
        {
            int counter = 0;
            int counter2 = 0;
            int counter3 = 0;
            int MAX = 200;
            Audit.Core.Configuration.Setup()
                .UseDynamicProvider(_ => _.OnInsert(ev =>
                {
                    System.Threading.Interlocked.Increment(ref counter);
                }))
                .WithCreationPolicy(EventCreationPolicy.InsertOnEnd)
                .WithAction(_ => _.OnEventSaving(ev =>
                {
                    System.Threading.Interlocked.Increment(ref counter2);
                }))
                .WithAction(_ => _.OnScopeCreated(ev =>
                {
                    System.Threading.Interlocked.Increment(ref counter3);
                }));

            var tasks = new List<Task>();
            for (int i = 0; i < MAX; i++)
            {
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    AuditScope.Log("LoginSuccess", new { username = "federico", id = i });
                    Audit.Core.Configuration.AddCustomAction(ActionType.OnEventSaving, ev =>
                    {
                        //do nothing, just bother
                        var d = ev.Event.Duration * 1234567;
                    });
                    AuditScope.CreateAndSave("LoginFailed", new { username = "adriano", id = i * -1 });
                }));
            }
            Task.WaitAll(tasks.ToArray());
            Assert.AreEqual(MAX * 2, counter);
            Assert.AreEqual(MAX * 2, counter2);
            Assert.AreEqual(MAX * 2, counter3);
        }



        [Test]
        public void Test_DynamicDataProvider()
        {
            int onInsertCount = 0, onReplaceCount = 0, onInsertOrReplaceCount = 0;
            Core.Configuration.Setup()
                .UseDynamicProvider(config => config
                    .OnInsert(ev => onInsertCount++)
                    .OnReplace((obj, ev) => onReplaceCount++)
                    .OnInsertAndReplace(ev => onInsertOrReplaceCount++));

            var scope = AuditScope.Create("et1", null, EventCreationPolicy.Manual);
            scope.Save();
            scope.SetCustomField("field", "value");
            Assert.AreEqual(1, onInsertCount);
            Assert.AreEqual(0, onReplaceCount);
            Assert.AreEqual(1, onInsertOrReplaceCount);
            scope.Save();
            Assert.AreEqual(1, onInsertCount);
            Assert.AreEqual(1, onReplaceCount);
            Assert.AreEqual(2, onInsertOrReplaceCount);
        }

        [Test]
        public void Test_TypeExtension()
        {
            var s = new List<Dictionary<HashSet<string>, KeyValuePair<int, decimal>>>();
            var fullname = s.GetType().GetFullTypeName();
            Assert.AreEqual("List<Dictionary<HashSet<String>,KeyValuePair<Int32,Decimal>>>", fullname);
        }

        [Test]
        public void Test_EntityFramework_Config_Precedence()
        {
            EntityFramework.Configuration.Setup()
                .ForContext<MyContext>(x => x.AuditEventType("ForContext"))
                .UseOptIn();
            EntityFramework.Configuration.Setup()
                .ForAnyContext(x => x.AuditEventType("ForAnyContext").IncludeEntityObjects(true).ExcludeValidationResults(true))
                .UseOptOut();

            var ctx = new MyContext();
            var ctx2 = new AnotherContext();

            Assert.AreEqual("FromAttr", ctx.AuditEventType);
            Assert.AreEqual(true, ctx.IncludeEntityObjects);
            Assert.AreEqual(true, ctx.ExcludeValidationResults);
            Assert.AreEqual(AuditOptionMode.OptIn, ctx.Mode);

            Assert.AreEqual("ForAnyContext", ctx2.AuditEventType);
            Assert.AreEqual(AuditOptionMode.OptOut, ctx2.Mode);
        }

        [Test]
        public void Test_FluentConfig_FileLog()
        {
            int x = 0;
            Core.Configuration.Setup()
                .UseFileLogProvider(config => config.Directory(@"C:\").FilenamePrefix("prefix"))
                .WithCreationPolicy(EventCreationPolicy.Manual)
                .WithAction(action => action.OnScopeCreated(s => x++));
            var scope = AuditScope.Create("test", null);
            scope.Dispose();
            Assert.AreEqual(typeof(FileDataProvider), Core.Configuration.DataProvider.GetType());
            Assert.AreEqual("prefix", (Core.Configuration.DataProvider as FileDataProvider).FilenamePrefix);
            Assert.AreEqual(@"C:\", (Core.Configuration.DataProvider as FileDataProvider).DirectoryPath);
            Assert.AreEqual(EventCreationPolicy.Manual, Core.Configuration.CreationPolicy);
            Assert.True(Core.Configuration.AuditScopeActions.ContainsKey(ActionType.OnScopeCreated));
            Assert.AreEqual(1, x);
        }
#if NET461 || NETCOREAPP2_0
        [Test]
        public void Test_FluentConfig_EventLog()
        {
            Core.Configuration.Setup()
                .UseEventLogProvider(config => config.LogName("LogName").SourcePath("SourcePath").MachineName("MachineName"))
                .WithCreationPolicy(EventCreationPolicy.Manual);
            var scope = AuditScope.Create("test", null);
            scope.Dispose();
            Assert.AreEqual(typeof(EventLogDataProvider), Core.Configuration.DataProvider.GetType());
            Assert.AreEqual("LogName", (Core.Configuration.DataProvider as EventLogDataProvider).LogName);
            Assert.AreEqual("SourcePath", (Core.Configuration.DataProvider as EventLogDataProvider).SourcePath);
            Assert.AreEqual("MachineName", (Core.Configuration.DataProvider as EventLogDataProvider).MachineName);
            Assert.AreEqual(EventCreationPolicy.Manual, Core.Configuration.CreationPolicy);
        }
#endif
        [Test]
        public void Test_StartAndSave()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.Serialize(It.IsAny<string>())).CallBase();

            var eventType = "event type";
            AuditScope.Log(eventType, new { ExtraField = "extra value" });

            AuditScope.CreateAndSave(eventType, new { Extra1 = new { SubExtra1 = "test1" }, Extra2 = "test2" }, provider.Object);
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Once);
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Never);

        }

        [Test]
        public void Test_CustomAction_OnCreating()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.Serialize(It.IsAny<string>())).CallBase();
            
            var eventType = "event type 1";
            var target = "test";
            Core.Configuration.AddOnCreatedAction(scope =>
            {
                scope.SetCustomField("custom field", "test");
                if (scope.EventType == eventType)
                {
                    scope.Discard();
                }
            });
            Core.Configuration.AddOnSavingAction(scope =>
            {
                Assert.True(false, "This should not be executed");
            });

            AuditEvent ev;
            using (var scope = AuditScope.Create(eventType, () => target, EventCreationPolicy.InsertOnStartInsertOnEnd, provider.Object))
            {
                ev = scope.Event;
            }
            Core.Configuration.ResetCustomActions();
            Assert.True(ev.CustomFields.ContainsKey("custom field"));
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Never);
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Never);
        }

        [Test]
        public void Test_CustomAction_OnSaving()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.Serialize(It.IsAny<string>())).CallBase();
            //provider.Setup(p => p.InsertEvent(It.IsAny<AuditEvent>())).Returns((AuditEvent e) => e.Comments);
            var eventType = "event type 1";
            var target = "test";
            var comment = "comment test";
            Core.Configuration.AddCustomAction(ActionType.OnEventSaving, scope =>
            {
                scope.Comment(comment);
            });
            AuditEvent ev;
            using (var scope = AuditScope.Create(eventType, () => target, EventCreationPolicy.Manual, provider.Object))
            {
                ev = scope.Event;
                scope.Save();
            }
            Core.Configuration.ResetCustomActions();
            Assert.True(ev.Comments.Contains(comment));
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Once);
        }

        [Test]
        public void Test_CustomAction_OnSaved()
        {
            object id = null;
            Core.Configuration.ResetCustomActions();
            Core.Configuration.Setup()
                .UseDynamicProvider(_ => _.OnInsert(ev => ev.EventType))
                .WithCreationPolicy(EventCreationPolicy.InsertOnStartReplaceOnEnd);
            Core.Configuration.AddCustomAction(ActionType.OnEventSaved, scope =>
            {
                id = scope.EventId;
            });
            using (var scope = AuditScope.Create("eventType as id", null))
            {
                scope.Discard();
            }
            Core.Configuration.ResetCustomActions();

            Assert.AreEqual("eventType as id", id);
        }

        [Test]
        public void Test_CustomAction_OnSaving_Discard()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.Serialize(It.IsAny<string>())).CallBase();
            var eventType = "event type 1";
            var target = "test";
            Audit.Core.Configuration.AddCustomAction(ActionType.OnEventSaving, scope =>
            {
                scope.Discard();
            });
            AuditEvent ev;
            using (var scope = AuditScope.Create(eventType, () => target, EventCreationPolicy.Manual, provider.Object))
            {
                ev = scope.Event;
                scope.Save();
            }
            Core.Configuration.ResetCustomActions();
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Never);
        }

        [Test]
        public void Test_CustomAction_OnCreating_Double()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.Serialize(It.IsAny<string>())).CallBase();
            var eventType = "event type 1";
            var target = "test";
            var key1 = "key1";
            var key2 = "key2";
            Core.Configuration.AddCustomAction(ActionType.OnScopeCreated, scope =>
            {
                scope.SetCustomField(key1, "test");
            });
            Core.Configuration.AddCustomAction(ActionType.OnScopeCreated, scope =>
            {
                scope.Event.CustomFields.Remove(key1);
                scope.SetCustomField(key2, "test");
            });
            AuditEvent ev;
            using (var scope = AuditScope.Create(eventType, () => target, EventCreationPolicy.Manual, provider.Object))
            {
                ev = scope.Event;
            }
            Core.Configuration.ResetCustomActions();
            Assert.False(ev.CustomFields.ContainsKey(key1));
            Assert.True(ev.CustomFields.ContainsKey(key2));
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Never);
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Never);
        }

        [Test]
        public void TestSave()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.Serialize(It.IsAny<string>())).CallBase();
            Core.Configuration.DataProvider = provider.Object;
            var target = "initial";
            var eventType = "SomeEvent";
            AuditEvent ev;
            using (var scope = AuditScope.Create(eventType, () => target, EventCreationPolicy.InsertOnEnd))
            {
                ev = scope.Event;
                scope.Comment("test");
                scope.SetCustomField<string>("custom", "value");
                target = "final";
                scope.Save(); // this should do nothing because of the creation policy (this no more true since v4.6.2)
                provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Once);
            }
            Assert.AreEqual(eventType, ev.EventType);
            Assert.True(ev.Comments.Contains("test"));
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Exactly(2));
        }

        [Test]
        public void Test_Dispose()
        {
            var provider = new Mock<AuditDataProvider>();

            using (var scope = AuditScope.Create(null, null, EventCreationPolicy.InsertOnEnd, provider.Object))
            {
            }

            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Exactly(1));
        }

        [Test]
        public void TestDiscard()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.Serialize(It.IsAny<string>())).CallBase();
            Core.Configuration.DataProvider = provider.Object;
            var target = "initial";
            var eventType = "SomeEvent";
            AuditEvent ev;
            using (var scope = AuditScope.Create(eventType, () => target, EventCreationPolicy.InsertOnEnd))
            {
                ev = scope.Event;
                scope.Comment("test");
                scope.SetCustomField<string>("custom", "value");
                target = "final";
                scope.Discard();
            }
            Assert.AreEqual(eventType, ev.EventType);
            Assert.True(ev.Comments.Contains("test"));
            Assert.Null(ev.Target.New);
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Never);
        }

        [Test]
        public void Test_EventCreationPolicy_InsertOnEnd()
        {
            var provider = new Mock<AuditDataProvider>();
            Core.Configuration.DataProvider = provider.Object;
            using (var scope = AuditScope.Create("SomeEvent", () => "target", EventCreationPolicy.InsertOnEnd))
            {
                scope.Comment("test");
                scope.Save(); // this should do nothing because of the creation policy (this is no more true, since v 4.6.2)
            }
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Never);
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Exactly(2));
        }

        [Test]
        public void Test_EventCreationPolicy_InsertOnStartReplaceOnEnd()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.InsertEvent(It.IsAny<AuditEvent>())).Returns(() => Guid.NewGuid());
            Core.Configuration.DataProvider = provider.Object;
            using (var scope = AuditScope.Create("SomeEvent", () => "target", EventCreationPolicy.InsertOnStartReplaceOnEnd))
            {
                scope.Comment("test");
            }
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Once);
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Once);
        }

        [Test]
        public void Test_EventCreationPolicy_InsertOnStartInsertOnEnd()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.InsertEvent(It.IsAny<AuditEvent>())).Returns(() => Guid.NewGuid());
            Core.Configuration.DataProvider = provider.Object;
            using (var scope = AuditScope.Create("SomeEvent", () => "target", EventCreationPolicy.InsertOnStartInsertOnEnd))
            {
                scope.Comment("test");
            }
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Never);
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Exactly(2));
        }

        [Test]
        public void Test_EventCreationPolicy_Manual()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.InsertEvent(It.IsAny<AuditEvent>())).Returns(() => Guid.NewGuid());
            Core.Configuration.DataProvider = provider.Object;
            using (var scope = AuditScope.Create("SomeEvent", () => "target", EventCreationPolicy.Manual))
            {
                scope.Comment("test");
            }
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Never);

            using (var scope = AuditScope.Create("SomeEvent", () => "target", EventCreationPolicy.Manual))
            {
                scope.Comment("test");
                scope.Save();
                scope.Comment("test2");
                scope.Save();
            }
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Once);
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Once);
        }

        [Test]
        public void Test_ExtraFields()
        {
            Core.Configuration.DataProvider = new FileDataProvider();
            var scope = AuditScope.Create("SomeEvent", null, new { @class = "class value", DATA = 123 }, EventCreationPolicy.Manual);
            scope.Comment("test");
            var ev = scope.Event;
            scope.Discard();
            Assert.AreEqual("123", ev.CustomFields["DATA"].ToString());
            Assert.AreEqual("class value", ev.CustomFields["class"].ToString());
        }

        [Test]
        public void Test_TwoScopes()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.InsertEvent(It.IsAny<AuditEvent>())).Returns(() => Guid.NewGuid());
            Core.Configuration.DataProvider = provider.Object;
            var scope1 = AuditScope.Create("SomeEvent1", null, new { @class = "class value1", DATA = 111 }, EventCreationPolicy.Manual);
            scope1.Save();
            var scope2 = AuditScope.Create("SomeEvent2", null, new { @class = "class value2", DATA = 222 }, EventCreationPolicy.Manual);
            scope2.Save();
            Assert.NotNull(scope1.EventId);
            Assert.NotNull(scope2.EventId);
            Assert.AreNotEqual(scope1.EventId, scope2.EventId);
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Exactly(2));
        }

        [AuditDbContext(AuditEventType = "FromAttr")]
        public class MyContext : AuditDbContext
        {
            public override string AuditEventType { get { return base.AuditEventType; } }
            public override bool IncludeEntityObjects { get { return base.IncludeEntityObjects; } }
            public override AuditOptionMode Mode { get { return base.Mode; } }
        }
        public class AnotherContext : AuditDbContext
        {
            public override string AuditEventType { get { return base.AuditEventType; } }
            public override bool IncludeEntityObjects { get { return base.IncludeEntityObjects; } }
            public override AuditOptionMode Mode { get { return base.Mode; } }
        }
        public class SomeClass
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }
}
