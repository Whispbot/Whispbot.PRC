using Newtonsoft.Json;
using Npgsql;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Whispbot.PRC.Databases
{
    public static class Postgres
    {
        private static NpgsqlDataSource? _dataSource = null;
        private static bool _initialized = false;
        private static DateTime _lastConnectionAttempt = DateTime.MinValue;
        private static readonly TimeSpan _reconnectInterval = TimeSpan.FromMinutes(2); // Retry after 2 minutes

        private static readonly TimeSpan _pingMeasureInterval = TimeSpan.FromMinutes(5);
        private static double _ping = -1d;
        private static bool _initializing = false;

        /// <summary>
        /// The database ping in ms
        /// </summary>
        public static double Ping
        {
            get
            {
                if (_ping < 0 || DateTime.UtcNow - _lastConnectionAttempt > _pingMeasureInterval)
                {
                    _ping = MeasurePing();
                }
                return _ping;
            }
        }

        public static double MeasurePing()
        {
            if (_dataSource == null)
            {
                return -1d;
            }
            double start = DateTimeOffset.UtcNow.UtcTicks;
            try
            {
                using var connection = _dataSource.OpenConnection();
                using var command = new NpgsqlCommand("SELECT 1", connection);
                command.ExecuteScalar();
                return (DateTimeOffset.UtcNow.UtcTicks - start) / 10000;
            }
            catch
            {
                return -1d;
            }
        }

        /// <summary>
        /// Gets a database connection from the connection pool. If the data source is not initialized,
        /// it will attempt to initialize if the last failed attempt was more than the reconnect interval ago.
        /// </summary>
        /// <returns>A database connection from the pool, or null if data source is not available</returns>
        public static NpgsqlConnection? GetConnection()
        {
            // If we have a data source, get a connection from the pool
            if (_initialized && _dataSource != null)
            {
                try
                {
                    return _dataSource.OpenConnection();
                }
                catch
                {
                    // If we can't get a connection, mark as not initialized and fall through to reinit
                    _initialized = false;
                }
            }

            // If we're not initialized and the last attempt was recent, return null
            if (!_initialized && DateTime.UtcNow - _lastConnectionAttempt < _reconnectInterval)
            {
                return null;
            }

            // Try to initialize
            if (Init())
            {
                try
                {
                    return _dataSource?.OpenConnection();
                }
                catch
                {
                    return null;
                }
            }

            // Initialization failed
            return null;
        }

        public static bool Init()
        {
            if (_initializing) return false;
            _initializing = true;
            double start = DateTimeOffset.UtcNow.UtcTicks;
            Log.Information("Initializing postgres connection pool...");
            _lastConnectionAttempt = DateTime.UtcNow;

            try
            {
                // Dispose existing data source if it exists
                if (_dataSource != null)
                {
                    try
                    {
                        _dataSource.Dispose();
                    }
                    catch { }
                }

                string? machineId = Environment.GetEnvironmentVariable("RAILWAY_REPLICA_ID");
                bool isDev = machineId is null;

                string? host = Environment.GetEnvironmentVariable($"DB_{(isDev ? "PUBLIC_" : "")}HOST");
                string? port = Environment.GetEnvironmentVariable($"DB_{(isDev ? "PUBLIC_" : "")}PORT");
                string? username = Environment.GetEnvironmentVariable("DB_USERNAME");
                string? password = Environment.GetEnvironmentVariable("DB_PASSWORD");
                string? database = Environment.GetEnvironmentVariable("DB_DATABASE");

                var missingVars = new List<string>();
                if (string.IsNullOrEmpty(host)) missingVars.Add("DB_HOST");
                if (string.IsNullOrEmpty(port)) missingVars.Add("DB_PORT");
                if (string.IsNullOrEmpty(username)) missingVars.Add("DB_USERNAME");
                if (string.IsNullOrEmpty(password)) missingVars.Add("DB_PASSWORD");
                if (string.IsNullOrEmpty(database)) missingVars.Add("DB_DATABASE");

                if (missingVars.Count > 0)
                {
                    Log.Fatal("ERROR: Missing required environment variables:");
                    foreach (var var in missingVars)
                    {
                        Log.Fatal($"  - {var}");
                    }
                    Log.Fatal("\nPlease set these environment variables and restart the application.");
                    Logger.Shutdown();

                    Environment.Exit(1);
                    return false;
                }

                // Build connection string with connection pooling parameters
                var connectionStringBuilder = new NpgsqlConnectionStringBuilder
                {
                    Host = host,
                    Port = int.Parse(port!),
                    Username = username,
                    Password = password,
                    Database = database,
                    Timeout = 15,
                    CommandTimeout = 30,
                    // Connection pooling settings
                    MinPoolSize = 5,
                    MaxPoolSize = 20,
                    ConnectionIdleLifetime = 300, // 5 minutes
                    ConnectionPruningInterval = 10 // 10 seconds
                };

                // Create data source with connection pooling
                var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionStringBuilder.ToString());
                _dataSource = dataSourceBuilder.Build();

                // Test the connection pool
                using (var connection = _dataSource.OpenConnection())
                {
                    using var command = new NpgsqlCommand("SELECT 1", connection);
                    command.ExecuteNonQuery();
                }

                Log.Information($"Postgres connection pool initialized in {(DateTimeOffset.UtcNow.UtcTicks - start) / 10000}ms");
                _initialized = true;
                _initializing = false;
                return true;
            }
            catch (NpgsqlException ex)
            {
                Log.Error($"Database connection pool initialization error: {ex.Message}");
                _initialized = false;
                _initializing = false;
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"Unexpected error during database connection pool initialization: {ex.Message}");
                _initialized = false;
                _initializing = false;
                return false;
            }
        }

        /// <summary>
        /// Checks if the connection pool is initialized and available
        /// </summary>
        /// <returns></returns>
        public static bool IsConnected()
        {
            return _initialized && _dataSource != null;
        }

        /// <summary>
        /// Checks if the connection pool is still valid by testing a connection
        /// </summary>
        public static bool IsConnectionValid()
        {
            if (_dataSource == null)
            {
                return false;
            }

            try
            {
                using var connection = _dataSource.OpenConnection();
                using var command = new NpgsqlCommand("SELECT 1", connection);
                command.ExecuteScalar();
                return true;
            }
            catch
            {
                _initialized = false;
                return false;
            }
        }

        private static NpgsqlCommand AddArgs(this NpgsqlCommand command, List<object> args)
        {
            int i = 1;
            foreach (var arg in args)
            {
                command.Parameters.AddWithValue($"@{i}", arg);
                i++;
            }
            return command;
        }

        public static NpgsqlTransaction? BeginTransaction()
        {
            var connection = GetConnection();
            if (connection is null) return null;

            return connection.BeginTransaction();
        }

        public static List<T>? Select<T>(string sql, List<object>? args = null, NpgsqlTransaction? transaction = null) where T : new()
        {
            NpgsqlConnection? connection = transaction?.Connection;
            bool connectionOwned = false;
            if (connection == null)
            {
                connection = GetConnection();
                if (connection is null) return null;
                connectionOwned = true;
            }

            try
            {
                using var command = new NpgsqlCommand(sql, connection, transaction);
                command.AddArgs(args ?? []);
                using var reader = command.ExecuteReader();
                return reader.ToList<T>();
            }
            finally
            {
                if (connectionOwned)
                {
                    try { connection.Dispose(); } catch { }
                }
            }
        }

        public static List<dynamic>? Select(string sql, List<object>? args = null, NpgsqlTransaction? transaction = null)
        {
            NpgsqlConnection? connection = transaction?.Connection;
            bool connectionOwned = false;
            if (connection == null)
            {
                connection = GetConnection();
                if (connection is null) return null;
                connectionOwned = true;
            }

            try
            {
                using var command = new NpgsqlCommand(sql, connection, transaction);
                command.AddArgs(args ?? []);
                using var reader = command.ExecuteReader();
                return reader.ToDynamicList();
            }
            finally
            {
                if (connectionOwned)
                {
                    try { connection.Dispose(); } catch { }
                }
            }
        }

        public static T? SelectFirst<T>(string sql, List<object>? args = null, NpgsqlTransaction? transaction = null) where T : new()
        {
            NpgsqlConnection? connection = transaction?.Connection;
            bool connectionOwned = false;
            if (connection == null)
            {
                connection = GetConnection();
                if (connection is null) return default;
                connectionOwned = true;
            }

            try
            {
                using var command = new NpgsqlCommand(sql, connection, transaction);
                command.AddArgs(args ?? []);
                using var reader = command.ExecuteReader();
                return reader.FirstOrDefault<T>();
            }
            finally
            {
                if (connectionOwned)
                {
                    try { connection.Dispose(); } catch { }
                }
            }
        }

        public static int Execute(string sql, List<object>? args = null, NpgsqlTransaction? transaction = null)
        {
            NpgsqlConnection? connection = transaction?.Connection;
            bool connectionOwned = false;
            if (connection == null)
            {
                connection = GetConnection();
                if (connection is null) return -1;
                connectionOwned = true;
            }

            try
            {
                using var command = new NpgsqlCommand(sql, connection, transaction);
                command.AddArgs(args ?? []);
                return command.ExecuteNonQuery();
            }
            finally
            {
                if (connectionOwned)
                {
                    try { connection.Dispose(); } catch { }
                }
            }
        }

        /// <summary>
        /// Disposes the connection pool and cleans up resources
        /// </summary>
        public static void Dispose()
        {
            try
            {
                _dataSource?.Dispose();
                _dataSource = null;
                _initialized = false;
                Log.Information("Postgres connection pool disposed");
            }
            catch (Exception ex)
            {
                Log.Error($"Error disposing postgres connection pool: {ex.Message}");
            }
        }
    }

    public class PostgresCount
    {
        public long count;
    }

    public static class PostgresExtensions
    {
        /// <summary>
        /// Converts all rows from a data reader to a list of objects of type T
        /// </summary>
        /// <typeparam name="T">The type to convert to</typeparam>
        /// <param name="reader">The data reader containing the results</param>
        /// <returns>A list of objects of type T</returns>
        public static List<T> ToList<T>(this NpgsqlDataReader reader) where T : new()
        {
            var result = new List<T>();
            var columnNames = GetColumnNames(reader);
            var mappings = GetMappings<T>();

            while (reader.Read())
            {
                var item = new T();
                MapReaderToObject(reader, item, columnNames, mappings);
                result.Add(item);
            }

            return result;
        }

        /// <summary>
        /// Converts the first row of a data reader to an object of type T
        /// </summary>
        /// <typeparam name="T">The type to convert to</typeparam>
        /// <param name="reader">The data reader containing the results</param>
        /// <returns>An object of type T, or default(T) if no rows exist</returns>
        public static T? FirstOrDefault<T>(this NpgsqlDataReader reader) where T : new()
        {
            if (!reader.Read())
                return default;

            var item = new T();
            var columnNames = GetColumnNames(reader);
            var mappings = GetMappings<T>();

            MapReaderToObject(reader, item, columnNames, mappings);

            return item;
        }

        public static List<object> ToDynamicList(this NpgsqlDataReader reader)
        {
            var result = new List<object>();
            var columnNames = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columnNames[i] = reader.GetName(i);
            }

            while (reader.Read())
            {
                IDictionary<string, object?> expando = new ExpandoObject();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    expando[columnNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                result.Add(expando);
            }

            return result;
        }

        /// <summary>
        /// Gets the column names from the data reader
        /// </summary>
        private static string[] GetColumnNames(IDataReader reader)
        {
            var columnNames = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columnNames[i] = reader.GetName(i);
            }
            return columnNames;
        }

        /// <summary>
        /// Gets mappings for properties and fields of type T
        /// </summary>
        private static (Dictionary<string, PropertyInfo> Properties, Dictionary<string, FieldInfo> Fields) GetMappings<T>()
        {
            var properties = typeof(T).GetProperties();
            var fields = typeof(T).GetFields();

            var propertyMap = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            var fieldMap = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in properties)
            {
                propertyMap[property.Name] = property;
            }

            foreach (var field in fields)
            {
                fieldMap[field.Name] = field;
            }

            return (propertyMap, fieldMap);
        }

        /// <summary>
        /// Maps data from a reader to an object
        /// </summary>
        private static void MapReaderToObject<T>(
            NpgsqlDataReader reader,
            T item,
            string[] columnNames,
            (Dictionary<string, PropertyInfo> Properties, Dictionary<string, FieldInfo> Fields) mappings)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = columnNames[i];

                if (reader.IsDBNull(i))
                    continue;

                var value = reader.GetValue(i);

                if (mappings.Properties.TryGetValue(columnName, out var property))
                {
                    try
                    {
                        SetPropertyValue(property, item!, value);
                    }
                    catch { }
                    continue;
                }

                if (mappings.Fields.TryGetValue(columnName, out var field))
                {
                    try
                    {
                        SetFieldValue(field, item!, value);
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Sets a property value with appropriate type conversion
        /// </summary>
        private static void SetPropertyValue(PropertyInfo property, object item, object value)
        {
            Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (propertyType == typeof(Guid) && value is string stringValue)
            {
                property.SetValue(item, Guid.Parse(stringValue));
            }
            else if (propertyType == typeof(Guid) && value is byte[] guidBytes)
            {
                property.SetValue(item, new Guid(guidBytes));
            }
            else if (propertyType == typeof(DateTimeOffset) && value is DateTime dateTimeValue)
            {
                property.SetValue(item, new DateTimeOffset(dateTimeValue));
            }
            else if (propertyType.IsEnum && value is string enumString)
            {
                property.SetValue(item, Enum.Parse(property.PropertyType, enumString));
            }
            else if (propertyType.IsEnum && value is int enumInt)
            {
                property.SetValue(item, Enum.ToObject(property.PropertyType, enumInt));
            }
            else if (propertyType.IsClass && propertyType != typeof(string) && value is string jsonString)
            {
                property.SetValue(item, JsonConvert.DeserializeObject(jsonString, propertyType));
            }
            else if (value is Array array)
            {
                if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var elementType = propertyType.GetGenericArguments()[0];
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    var list = Activator.CreateInstance(listType);
                    var addMethod = listType.GetMethod("Add");

                    foreach (var arrayElement in array)
                    {
                        if (arrayElement != null)
                        {
                            var convertedItem = elementType == typeof(string) ? arrayElement.ToString() : Convert.ChangeType(arrayElement, elementType);
                            addMethod?.Invoke(list, [convertedItem]);
                        }
                    }

                    property.SetValue(item, list);
                }
                else
                {
                    property.SetValue(item, array);
                }
            }
            else
            {
                var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var convertedValue = Convert.ChangeType(value, targetType);
                property.SetValue(item, convertedValue);
            }
        }

        /// <summary>
        /// Sets a field value with appropriate type conversion
        /// </summary>
        private static void SetFieldValue(FieldInfo field, object item, object value)
        {
            Type fieldType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;

            if (fieldType == typeof(Guid) && value is string stringValue)
            {
                field.SetValue(item, Guid.Parse(stringValue));
            }
            else if (fieldType == typeof(Guid) && value is byte[] guidBytes)
            {
                field.SetValue(item, new Guid(guidBytes));
            }
            else if (fieldType == typeof(DateTimeOffset) && value is DateTime dateTimeValue)
            {
                field.SetValue(item, new DateTimeOffset(dateTimeValue));
            }
            else if (fieldType.IsEnum && value is string enumString)
            {
                field.SetValue(item, Enum.Parse(field.FieldType, enumString));
            }
            else if (fieldType.IsEnum && value is int enumInt)
            {
                field.SetValue(item, Enum.ToObject(field.FieldType, enumInt));
            }
            else if (fieldType.IsClass && fieldType != typeof(string) && value is string jsonString)
            {
                field.SetValue(item, JsonConvert.DeserializeObject(jsonString, fieldType));
            }
            else if (value is Array array)
            {
                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var elementType = fieldType.GetGenericArguments()[0];
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    var list = Activator.CreateInstance(listType);
                    var addMethod = listType.GetMethod("Add");

                    foreach (var arrayElement in array)
                    {
                        if (arrayElement != null)
                        {
                            var convertedItem = elementType == typeof(string) ? arrayElement.ToString() : Convert.ChangeType(arrayElement, elementType);
                            addMethod?.Invoke(list, [convertedItem]);
                        }
                    }

                    field.SetValue(item, list);
                }
                else
                {
                    field.SetValue(item, array);
                }
            }
            else
            {
                var targetType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
                var convertedValue = Convert.ChangeType(value, targetType);
                field.SetValue(item, convertedValue);
            }
        }

        public static void OpenWithRetry(this NpgsqlConnection connection, int maxRetries, TimeSpan delay)
        {
            int retries = 0;
            while (true)
            {
                try
                {
                    connection.Open();
                    return;
                }
                catch (NpgsqlException)
                {
                    if (++retries > maxRetries)
                        throw;

                    Thread.Sleep(delay);
                }
            }
        }
    }
}