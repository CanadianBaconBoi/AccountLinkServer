using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace OblivionUtils.Services
{
    public class DatabaseManager
    {
        public DatabaseInformation databaseInformation;

        public DatabaseManager(DatabaseInformation _databaseInformation)
        {
            databaseInformation = _databaseInformation;
        }

        public async Task initialize()
        {
            try
            {
                using (var connection = new MySqlConnection(databaseInformation.GetConnectionString()))
                {
                    await connection.OpenAsync();
                    await OnDatabaseInitialized?.Invoke(connection)!;
                    await connection.CloseAsync();
                }
            }
            catch (MySqlException e)
            {
                Logger.Log(Microsoft.Extensions.Logging.LogLevel.Critical, $"Failed to connect to MySQL Database {databaseInformation.Database}");
                throw new System.Exception("Connection Failure", e);
            }
        }

        public async Task<Int32> ExecuteParameterizedUpdateStatement(string query, Dictionary<string, Object> values)
        {
            using (var connection = new MySqlConnection(databaseInformation.GetConnectionString()))
            {
                await connection.OpenAsync();
                using var command = new MySqlCommand();
                command.Connection = connection;
                command.CommandText = query;
                foreach ((string name, object value) in values)
                {
                    command.Parameters.AddWithValue(name, value);
                }
                return await command.ExecuteNonQueryAsync();
            }
        }

        public async Task<Int32> ExecuteRawUpdateStatement(string query)
        {
            using (var connection = new MySqlConnection(databaseInformation.GetConnectionString()))
            {
                await connection.OpenAsync();
                using var command = new MySqlCommand();
                command.Connection = connection;
                command.CommandText = query;
                return await command.ExecuteNonQueryAsync();
            }
        }

        public async Task<Object?> ExecuteParameterizedScalarQueryStatement(string query, Dictionary<string, Object> values)
        {
            using (var connection = new MySqlConnection(databaseInformation.GetConnectionString()))
            {
                await connection.OpenAsync();
                using var command = new MySqlCommand();
                command.Connection = connection;
                command.CommandText = query;
                foreach ((string name, object value) in values)
                {
                    command.Parameters.AddWithValue(name, value);
                }
                return await command.ExecuteScalarAsync();
            }
        }

        public async Task<Object?> ExecuteRawScalarQueryStatement(string query)
        {
            using (var connection = new MySqlConnection(databaseInformation.GetConnectionString()))
            {
                await connection.OpenAsync();
                using var command = new MySqlCommand();
                command.Connection = connection;
                command.CommandText = query;
                return await command.ExecuteScalarAsync();
            }
        }
       
        public async Task<List<T>> ExecuteParameterizedQueryStatement<T>(string query, Dictionary<string, Object> values) where T : class, new()
        {
            using (var connection = new MySqlConnection(databaseInformation.GetConnectionString()))
            {
                await connection.OpenAsync();
                using var command = new MySqlCommand();
                command.Connection = connection;
                command.CommandText = query;
                foreach ((string name, object value) in values)
                {
                    command.Parameters.AddWithValue(name, value);
                }
                var reader = await command.ExecuteReaderAsync();

                List<T> table = new();

                FieldInfo[] fields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public);

                while (reader.Read())
                {
                    T t = new T();
                    foreach (FieldInfo field in fields)
                    {
                        var ordinal = reader.GetOrdinal(field.Name);
                        var val = reader.GetValue(ordinal);
                        field.SetValue(t, val);
                    }
                    table.Add(t);
                }

                return table;
            }
        }

        public async Task<List<T>> ExecuteRawQueryStatement<T>(string query) where T : class, new()
        {
            using (var connection = new MySqlConnection(databaseInformation.GetConnectionString()))
            {
                await connection.OpenAsync();
                using var command = new MySqlCommand();
                command.Connection = connection;
                command.CommandText = query;
                var reader = await command.ExecuteReaderAsync();

                List<T> table = new();

                FieldInfo[] fields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public);

                while (reader.Read())
                {
                    T t = new T();
                    foreach (FieldInfo field in fields)
                    {
                        var ordinal = reader.GetOrdinal(field.Name);
                        var val = reader.GetValue(ordinal);
                        field.SetValue(t, val);
                    }
                    table.Add(t);
                }

                return table;
            }
        }

        public event Func<MySqlConnection, Task> OnDatabaseInitialized;
    }

    public struct DatabaseInformation
    {
        public string Host { get; init; }
        public ushort Port { get; init; }
        public string User { get; init; }
        public string Password { get; init; }
        public string Database { get; init; }

        public string GetConnectionString()
        {
            return $"Host={Host};Port={Port};Username={User};Password={Password};Database={Database}";
        }
    }
}