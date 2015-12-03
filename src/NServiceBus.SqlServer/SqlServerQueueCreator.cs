namespace NServiceBus.Transports.SQLServer
{
    using System.Data;
    using System.Data.SqlClient;
    using System.Threading.Tasks;

    class SqlServerQueueCreator : ICreateQueues
    {
        readonly SqlConnectionFactory connectionFactory;
        readonly SqlServerAddressProvider addressProvider;

        public SqlServerQueueCreator(SqlConnectionFactory connectionFactory, SqlServerAddressProvider addressProvider)
        {
            this.connectionFactory = connectionFactory;
            this.addressProvider = addressProvider;
        }

        public async Task CreateQueueIfNecessary(QueueBindings queueBindings, string identity)
        {
            using (var connection = await connectionFactory.OpenNewConnection())
            {
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var receivingAddress in queueBindings.ReceivingAddresses)
                    {
                        await CreateQueue(addressProvider.Parse(receivingAddress), connection, transaction);
                    }
                    foreach (var receivingAddress in queueBindings.SendingAddresses)
                    {
                        await CreateQueue(addressProvider.Parse(receivingAddress), connection, transaction);
                    }

                    transaction.Commit();
                }
            }
        }

        async Task CreateQueue(SqlServerAddress address, SqlConnection connection, SqlTransaction transaction)
        {
            var sql = string.Format(Sql.CreateQueueText, address.SchemaName, address.TableName);

            using (var command = new SqlCommand(sql, connection, transaction)
            {
                CommandType = CommandType.Text
            })
            {
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}