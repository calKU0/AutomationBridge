using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace AutomationBridge.PTLWorker.Data
{
    public class PTLRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<PTLRepository> _logger;

        public PTLRepository(IConfiguration configuration, ILogger<PTLRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnectionString")
                ?? throw new InvalidOperationException("Missing DefaultConnectionString in configuration.");
            _logger = logger;
        }

        private async Task<SqlConnection> GetOpenConnectionAsync()
        {
            var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            return conn;
        }

        public async Task<IEnumerable<dynamic>> GetPTLStatusRowsAsync()
        {
            const string sql = @"SELECT ptl_jlCode, ptl_operation, ptl_color, ptl_text FROM dbo.PTLStatus";

            try
            {
                using var conn = await GetOpenConnectionAsync();
                return await conn.QueryAsync(sql);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing GetPTLStatusRowsAsync");
                return Array.Empty<dynamic>();
            }
        }

        public async Task<IEnumerable<dynamic>> GetJlsToPackAsync()
        {
            try
            {
                using var conn = await GetOpenConnectionAsync();
                return await conn.QueryAsync("dbo.KuwetyDoSpakowania", commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing GetJlsToPackAsync");
                return Array.Empty<dynamic>();
            }
        }

        public async Task<IEnumerable<dynamic>> GetRedJlsToPack()
        {
            try
            {
                using var conn = await GetOpenConnectionAsync();
                return await conn.QueryAsync("dbo.KuwetyDoSpakowaniaCzerwone", commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing GetRedJlsToPack");
                return Array.Empty<dynamic>();
            }
        }

        public async Task<string?> GetJlIdByCodeAsync(string code)
        {
            const string sql = "SELECT jlt_id FROM wms_jl WITH(NOLOCK) WHERE jlt_kod = @code";

            try
            {
                using var conn = await GetOpenConnectionAsync();
                var result = await conn.ExecuteScalarAsync(sql, new { code });
                return result?.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while getting JlId for code {Code}", code);
                return null;
            }
        }

        public async Task<string?> GetJlStatusAsync(string code)
        {
            const string sql = "SELECT ptl_operation FROM dbo.PTLStatus WHERE ptl_jlCode = @code";

            try
            {
                using var conn = await GetOpenConnectionAsync();
                var result = await conn.ExecuteScalarAsync(sql, new { code });
                return result?.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while getting JL status for code {Code}", code);
                return null;
            }
        }

        /// <summary>
        /// Updates PTLStatus table and calls wms_atrwartosc stored procedure in a transaction.
        /// Returns true if committed successfully.
        /// </summary>
        public async Task<bool> UpdateStatusAsync(int jlId, string operation, string? text, string plcStatus)
        {
            try
            {
                using var conn = await GetOpenConnectionAsync();
                using var tran = conn.BeginTransaction();

                try
                {
                    const string updateSql = @"
                        UPDATE dbo.PTLStatus
                        SET ptl_operation = @operation,
                            ptl_text = @text,
                            ptl_color = NULL
                        WHERE ptl_jlid = @jlid";

                    await conn.ExecuteAsync(updateSql, new { jlid = jlId, operation, text }, transaction: tran);

                    // Call the stored procedure
                    await conn.ExecuteAsync(
                        "dbo.wms_atrwartosc",
                        new
                        {
                            obotyp = 9140001,
                            oboid = jlId,
                            obolp = 0,
                            atrid = 44,
                            wartosc = plcStatus
                        },
                        commandType: CommandType.StoredProcedure,
                        transaction: tran
                    );

                    tran.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    tran.Rollback();
                    _logger.LogError(ex, "Error updating status for JL {JlId}. Rolled back.", jlId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error acquiring connection for UpdateStatusAsync");
                return false;
            }
        }
    }
}