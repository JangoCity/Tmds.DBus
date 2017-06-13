using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tmds.DBus.Tests
{
    public class ServiceNameTests
    {
        [Fact]
        public async Task Register()
        {
            using (var dbusDaemon = new DBusDaemon())
            {
                string serviceName = "tmds.dbus.test";

                await dbusDaemon.StartAsync();
                var address = dbusDaemon.Address;

                var conn1 = new Connection(address);
                await conn1.ConnectAsync();

                await conn1.RegisterServiceAsync(serviceName);

                bool released = await conn1.UnregisterServiceAsync(serviceName);
                Assert.Equal(true, released);
            }
        }

        [Fact]
        public async Task NameAlreadyRegisteredOnOtherConnection()
        {
            using (var dbusDaemon = new DBusDaemon())
            {
                string serviceName = "tmds.dbus.test";

                await dbusDaemon.StartAsync();
                var address = dbusDaemon.Address;

                var conn1 = new Connection(address);
                await conn1.ConnectAsync();
                await conn1.RegisterServiceAsync(serviceName, ServiceRegistrationOptions.None);

                var conn2 = new Connection(address);
                await conn2.ConnectAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(() => conn2.RegisterServiceAsync(serviceName, ServiceRegistrationOptions.None));
            }
        }

        [Fact]
        public async Task NameAlreadyRegisteredOnSameConnection()
        {
            using (var dbusDaemon = new DBusDaemon())
            {
                string serviceName = "tmds.dbus.test";

                await dbusDaemon.StartAsync();
                var address = dbusDaemon.Address;

                var conn1 = new Connection(address);
                await conn1.ConnectAsync();
                await conn1.RegisterServiceAsync(serviceName, ServiceRegistrationOptions.None);
                await Assert.ThrowsAsync<InvalidOperationException>(() => conn1.RegisterServiceAsync(serviceName, ServiceRegistrationOptions.None));
            }
        }

        [Fact]
        public async Task ReplaceRegistered()
        {
            using (var dbusDaemon = new DBusDaemon())
            {
                string serviceName = "tmds.dbus.test";

                await dbusDaemon.StartAsync();
                var address = dbusDaemon.Address;

                var conn1 = new Connection(address);
                await conn1.ConnectAsync();
                await conn1.RegisterServiceAsync(serviceName, ServiceRegistrationOptions.AllowReplacement);

                var conn2 = new Connection(address);
                await conn2.ConnectAsync();
                await conn2.RegisterServiceAsync(serviceName, ServiceRegistrationOptions.ReplaceExisting);
            }
        }

        [Fact]
        public async Task EmitLost()
        {
            using (var dbusDaemon = new DBusDaemon())
            {
                string serviceName = "tmds.dbus.test";

                await dbusDaemon.StartAsync();
                var address = dbusDaemon.Address;

                var conn1 = new Connection(address);
                await conn1.ConnectAsync();
                var onLost = new ObservableAction();
                await conn1.RegisterServiceAsync(serviceName, onLost.Action, ServiceRegistrationOptions.AllowReplacement);

                var conn2 = new Connection(address);
                await conn2.ConnectAsync();
                await conn2.RegisterServiceAsync(serviceName, ServiceRegistrationOptions.ReplaceExisting);

                await onLost.AssertNumberOfCallsAsync(1);
            }
        }

        [Fact]
        public async Task EmitAquired()
        {
            using (var dbusDaemon = new DBusDaemon())
            {
                string serviceName = "tmds.dbus.test";

                await dbusDaemon.StartAsync();
                var address = dbusDaemon.Address;

                var conn1 = new Connection(address);
                await conn1.ConnectAsync();
                var conn1OnLost = new ObservableAction();
                var conn1OnAquired = new ObservableAction();
                await conn1.QueueServiceRegistrationAsync(serviceName, conn1OnAquired.Action, conn1OnLost.Action, ServiceRegistrationOptions.AllowReplacement);
                await conn1OnAquired.AssertNumberOfCallsAsync(1);
                await conn1OnLost.AssertNumberOfCallsAsync(0);

                var conn2 = new Connection(address);
                await conn2.ConnectAsync();
                var conn2OnLost = new ObservableAction();
                var conn2OnAquired = new ObservableAction();
                await conn2.QueueServiceRegistrationAsync(serviceName, conn2OnAquired.Action, conn2OnLost.Action);
                await conn1OnAquired.AssertNumberOfCallsAsync(1);
                await conn1OnLost.AssertNumberOfCallsAsync(1);
                await conn2OnAquired.AssertNumberOfCallsAsync(1);
                await conn2OnLost.AssertNumberOfCallsAsync(0);
            }
        }

        [Fact]
        public async Task ResolveService()
        {
            using (var dbusDaemon = new DBusDaemon())
            {
                string serviceName = "tmds.dbus.test";

                await dbusDaemon.StartAsync();
                var address = dbusDaemon.Address;

                IConnection conn1 = new Connection(address);
                await conn1.ConnectAsync();

                IConnection conn2 = new Connection(address);
                await conn2.ConnectAsync();

                var owner = await conn2.ResolveServiceOwnerAsync(serviceName);
                Assert.Equal(null, owner);

                await conn1.RegisterServiceAsync(serviceName);
                owner = await conn2.ResolveServiceOwnerAsync(serviceName);
                Assert.Equal(conn1.LocalName, owner);

                await conn1.UnregisterServiceAsync(serviceName);
                owner = await conn2.ResolveServiceOwnerAsync(serviceName);
                Assert.Equal(null, owner);
            }
        }

        private static bool IsTravis = System.Environment.GetEnvironmentVariable("TRAVIS") == "true";

        [Theory]
        [InlineData("tmds.dbus.test", false)]
        [InlineData("tmds.dbus.test.*", false)]
        [InlineData("tmds.dbus.*", false)]
        [InlineData("tmds.*", false)]
        [InlineData(".*", true)]
        [InlineData("*", true)]
        public async Task WatchResolveService(string resolvedService, bool filterEvents)
        {
            using (var dbusDaemon = new DBusDaemon())
            {
                string serviceName = "tmds.dbus.test";

                await dbusDaemon.StartAsync();
                var address = dbusDaemon.Address;

                IConnection conn1 = new Connection(address);
                await conn1.ConnectAsync();

                IConnection conn2 = new Connection(address);
                await conn2.ConnectAsync();

                var cts = new CancellationTokenSource();
                cts.CancelAfter(IsTravis ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(10));
                var ct = cts.Token;

                var changeEvents = new BlockingCollection<ServiceOwnerChangedEventArgs>(new ConcurrentQueue<ServiceOwnerChangedEventArgs>());
                Action<ServiceOwnerChangedEventArgs> onChange =
                    change => { if (!filterEvents || (change.ServiceName == serviceName)) changeEvents.Add(change); };
                var resolver = await conn2.ResolveServiceOwnerAsync(resolvedService, onChange);

                await conn1.RegisterServiceAsync(serviceName);
                var e = changeEvents.Take(ct);
                Assert.Equal(serviceName, e.ServiceName);
                Assert.Equal(null, e.OldOwner);
                Assert.Equal(conn1.LocalName, e.NewOwner);

                await conn1.UnregisterServiceAsync(serviceName);
                e = changeEvents.Take(ct);
                Assert.Equal(serviceName, e.ServiceName);
                Assert.Equal(conn1.LocalName, e.OldOwner);
                Assert.Equal(null, e.NewOwner);

                resolver.Dispose();
                await conn1.RegisterServiceAsync(serviceName);
                resolver = await conn2.ResolveServiceOwnerAsync(resolvedService, onChange);

                e = changeEvents.Take(ct);
                Assert.Equal(serviceName, e.ServiceName);
                Assert.Equal(null, e.OldOwner);
                Assert.Equal(conn1.LocalName, e.NewOwner);
            }
        }
    }
}