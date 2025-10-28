using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutomationBridge.PLCWorker.Data
{
    public class DestinationResolver
    {
        private readonly IDbConnection _connection;
        private readonly ILogger<DestinationResolver> _logger;

        public DestinationResolver(IDbConnection connection, ILogger<DestinationResolver> logger)
        {
            _connection = connection;
            _logger = logger;
        }

        public async Task<string> ResolveDestination(string packageNumber, string dumpOccupancy, string scannerNumber)
        {
            try
            {
                // Ensure the connection is open
                if (_connection.State != ConnectionState.Open)
                    await ((SqlConnection)_connection).OpenAsync();

                var parameters = new DynamicParameters();
                parameters.Add("@skan", packageNumber, DbType.String);
                parameters.Add("@skaner", scannerNumber, DbType.String);
                parameters.Add("@zajetoscZrzutow", dumpOccupancy, DbType.String);

                string? destination = await _connection.ExecuteScalarAsync<string>("dbo.GaskaPrzypiszTrasePLC", parameters, commandType: CommandType.StoredProcedure);

                return string.IsNullOrEmpty(destination) ? "000L" : destination;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error executing GaskaPrzypiszTrasePLC stored procedure");
                return "000L";
            }
            finally
            {
                if (_connection.State == ConnectionState.Open)
                    _connection.Close();
            }
        }
    }
}