﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CompatibilityTests.Common;
using CompatibilityTests.Common.Messages;
using NServiceBus;
using NServiceBus.Config;
using NServiceBus.Features;
using NServiceBus.Support;
using NServiceBus.Unicast.Subscriptions.MessageDrivenSubscriptions;

class EndpointFacade : MarshalByRefObject, IEndpointFacade, IEndpointConfigurationV1
{
    IBus bus;
    IStartableBus startableBus;
    MessageStore messageStore;
    CallbackResultStore callbackResultStore;
    SubscriptionStore subscriptionStore;
    Configure configure;
    List<ConnectionStringOverrides> connectionStringsOverrides = new List<ConnectionStringOverrides>();
    CustomConfiguration customConfiguration;
    string connectionString;

    public void Dispose()
    {
        startableBus.Dispose();
    }

    public IEndpointConfiguration Bootstrap(EndpointDefinition endpointDefinition)
    {
        if (endpointDefinition.MachineName != null)
        {
            RuntimeEnvironment.MachineNameAction = () => endpointDefinition.MachineName;
        }

        configure = Configure.With();
        configure.DefaultBuilder();

        configure.DefineEndpointName(endpointDefinition.Name);
        Address.InitializeLocalAddress(endpointDefinition.Name);

        configure.DefiningMessagesAs(t => t.Namespace != null && t.Namespace.EndsWith(".Messages") && t != typeof(TestEvent));
        configure.DefiningEventsAs(t => t == typeof(TestEvent));

        configure.UseInMemoryTimeoutPersister();
        configure.InMemorySubscriptionStorage();

        customConfiguration = new CustomConfiguration();
        configure.CustomConfigurationSource(customConfiguration);

        Feature.Enable<MessageDrivenSubscriptions>();

        configure.Configurer.ConfigureComponent<MessageStore>(DependencyLifecycle.SingleInstance);

        return this;
    }

    public void UseConnectionString(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public void MapMessageToEndpoint(Type messageType, string destination)
    {
        customConfiguration.AddMapping(new MessageEndpointMapping
        {Endpoint = destination, Messages = messageType.AssemblyQualifiedName});
    }

    public void Start()
    {
        var configFile = AppConfigGenerator.Generate(connectionString, connectionStringsOverrides);

        //HINT: we need to generate custom app.config because v1 sqltransports does a direct read from ConfigurationManager
        using (AppConfig.Change(configFile.FullName))
        {
            configure.UseTransport<SqlServer>();

            startableBus = configure.UnicastBus().CreateBus();
            bus = startableBus.Start(() => configure.ForInstallationOn<NServiceBus.Installation.Environments.Windows>().Install());

            messageStore = (MessageStore)configure.Builder.Build(typeof(MessageStore));
            subscriptionStore = new SubscriptionStore();
            callbackResultStore = new CallbackResultStore();

            configure.Builder.Build<MessageDrivenSubscriptionManager>().ClientSubscribed += (sender, args) => { subscriptionStore.Increment(); };
        }
    }

    public void ConfigureNamedConnectionStringForAddress(string endpoint, string connectionString)
    {
        connectionStringsOverrides.Add(new ConnectionStringOverrides
        {
            Address = endpoint,
            ConnectionString = connectionString
        });
    }

    public void SendCommand(Guid messageId)
    {
        bus.Send(new TestCommand { Id = messageId });
    }

    public void SendRequest(Guid requestId)
    {
        bus.Send(new TestRequest { RequestId = requestId });
    }

    public void PublishEvent(Guid eventId)
    {
        bus.Publish<TestEvent>(e => e.EventId = eventId);
    }

    public void SendAndCallbackForInt(int value)
    {
        Task.Run(async () =>
        {
            var callback = bus.Send(new TestIntCallback { Response = value });

            var res = await callback.Register();

            callbackResultStore.Add(res);
        });
    }

    public void SendAndCallbackForEnum(CallbackEnum value)
    {
        Task.Run(async () =>
        {
            var res = await bus.Send(new TestEnumCallback { CallbackEnum = value }).Register<CallbackEnum>();

            callbackResultStore.Add(res);
        });
    }

    public Guid[] ReceivedMessageIds => messageStore.GetAll();

    public Guid[] ReceivedResponseIds => messageStore.Get<TestResponse>();

    public Guid[] ReceivedEventIds => messageStore.Get<TestEvent>();

    public int[] ReceivedIntCallbacks => callbackResultStore.Get<int>();

    public CallbackEnum[] ReceivedEnumCallbacks => callbackResultStore.Get<CallbackEnum>();

    public int NumberOfSubscriptions => subscriptionStore.NumberOfSubscriptions;

}
