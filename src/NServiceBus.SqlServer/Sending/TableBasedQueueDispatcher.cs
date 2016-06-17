namespace NServiceBus.Transport.SQLServer
{
    using System.Collections.Generic;
    using System.Data.SqlClient;
    using System.Threading.Tasks;
    using System.Transactions;
    using Extensibility;
    using Transports;

    class TableBasedQueueDispatcher : IQueueDispatcher
    {
        SqlConnectionFactory connectionFactory;

        public TableBasedQueueDispatcher(SqlConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory;
        }

        public async Task DispatchAsIsolated(List<MessageWithAddress> operations)
        {
            if (operations.Count == 0)
            {
                return;
            }
            using (var scope = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
            using (var connection = await connectionFactory.OpenNewConnection().ConfigureAwait(false))
            {
                await Send(operations, connection, null).ConfigureAwait(false);

                scope.Complete();
            }
        }

        public async Task DispatchAsNonIsolated(List<MessageWithAddress> operations, ContextBag context)
        {
            if (operations.Count == 0)
            {
                return;
            }
            TransportTransaction transportTransaction;
            var transportTransactionExists = context.TryGet(out transportTransaction);

            if (transportTransactionExists == false || InReceiveOnlyTransportTransactionMode(transportTransaction))
            {
                await DispatchOperationsWithNewConnectionAndTransaction(operations).ConfigureAwait(false);
                return;
            }

            await DispatchUsingReceiveTransaction(transportTransaction, operations).ConfigureAwait(false);
        }


        async Task DispatchOperationsWithNewConnectionAndTransaction(List<MessageWithAddress> operations)
        {
            using (var connection = await connectionFactory.OpenNewConnection().ConfigureAwait(false))
            {
                if (operations.Count == 1)
                {
                    await Send(operations, connection, null).ConfigureAwait(false);
                    return;
                }

                using (var transaction = connection.BeginTransaction())
                {
                    await Send(operations, connection, transaction).ConfigureAwait(false);
                    transaction.Commit();
                }
            }
        }

        async Task DispatchUsingReceiveTransaction(TransportTransaction transportTransaction, List<MessageWithAddress> operations)
        {
            SqlConnection sqlTransportConnection;
            SqlTransaction sqlTransportTransaction;
            Transaction ambientTransaction;

            transportTransaction.TryGet(out sqlTransportConnection);
            transportTransaction.TryGet(out sqlTransportTransaction);
            transportTransaction.TryGet(out ambientTransaction);

            if (ambientTransaction != null)
            {
                using (var connection = await connectionFactory.OpenNewConnection().ConfigureAwait(false))
                {
                    await Send(operations, connection, null).ConfigureAwait(false);
                }
            }
            else
            {
                await Send(operations, sqlTransportConnection, sqlTransportTransaction).ConfigureAwait(false);
            }
        }

        static async Task Send(List<MessageWithAddress> operations, SqlConnection connection, SqlTransaction transaction)
        {
            foreach (var operation in operations)
            {
                var queue = new TableBasedQueue(operation.Address);
                await queue.Send(operation.Message, connection, transaction).ConfigureAwait(false);
            }
        }

        static bool InReceiveOnlyTransportTransactionMode(TransportTransaction transportTransaction)
        {
            bool inReceiveMode;
            return transportTransaction.TryGet(ReceiveWithReceiveOnlyTransaction.ReceiveOnlyTransactionMode, out inReceiveMode);
        }
    }
}