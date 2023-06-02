﻿using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace abkr.CatalogManager
{
    internal class RecordManager
    {


        public static void CreateDatabase(string databaseName, IMongoClient _client, CatalogManager _catalogManager)
        {
            _client?.GetDatabase(databaseName);
            Console.WriteLine($"Creating database: {databaseName}");
            _catalogManager?.CreateDatabase(databaseName);
        }

        public static void CreateTable(string databaseName, string tableName, List<Column> columns, CatalogManager _catalogManager, IMongoClient _client)
        {

            if (string.IsNullOrEmpty(databaseName))
            {
                throw new ArgumentNullException(nameof(databaseName), "Database name cannot be null or empty.");
            }

            if (string.IsNullOrEmpty(tableName))
            {
                throw new ArgumentNullException(nameof(tableName), "Table name cannot be null or empty.");
            }

            if (columns == null || columns.Count == 0)
            {
                throw new ArgumentNullException(nameof(columns), "Columns cannot be null or empty.");
            }

            var database = (_client?.GetDatabase(databaseName))
                ?? throw new Exception("Database connection not established.");


            database?.CreateCollection(tableName);
            Console.WriteLine($"Creating table: {databaseName}.{tableName}");

            //var columnsDictionary = columns?.ToDictionary(column => column.Name, column => column.Type);
            var primaryKeyColumn = columns.FirstOrDefault(column => column.IsPrimaryKey)?.Name
                ?? throw new Exception("RecordManager.CreateTable: Primary key not found.");
            var uniqueKeys = columns.Where(column => column.IsUnique).Select(column => column.Name).ToList();
            var foreignKeys = columns?.Where(column => !string.IsNullOrEmpty(column.ForeignKeyReference))?.ToDictionary(column => column.Name, column => column.ForeignKeyReference);

            _catalogManager?.CreateTable(databaseName, tableName, columns, primaryKeyColumn, foreignKeys, uniqueKeys);

            // Create index files for unique keys
            foreach (var column in columns)
            {
                if (column.IsUnique && !column.IsPrimaryKey)
                {
                    Console.WriteLine("RecordManager: Creating unique index");
                    _catalogManager?.CreateIndex(databaseName, tableName, column.Name, new List<string> { column.Name }, true);
                }
                else
                {
                    Console.WriteLine("RecordManager: Creating nonunique index");
                    _catalogManager?.CreateIndex(databaseName, tableName, column.Name, new List<string> { column.Name }, false);
                }
            }

            // If there's a foreign key column, create an index for it.
            foreach (var foreignKey in foreignKeys)
            {
                Console.WriteLine($"Creating foreign key:{foreignKey.Key}");
                CreateIndex(databaseName, tableName, $"{foreignKey.Key}_fk", new BsonArray(new[] { foreignKey.Key }), false, _client, _catalogManager);
            }
        }

        public static void CreateIndex(string databaseName, string tableName, string indexName, BsonArray columns, bool isUnique, IMongoClient _client, CatalogManager _catalogManager)
        {
            var collection = _client?.GetDatabase(databaseName).GetCollection<BsonDocument>(tableName);

            var indexKeysBuilder = new IndexKeysDefinitionBuilder<BsonDocument>();
            var indexKeysDefinitions = new List<IndexKeysDefinition<BsonDocument>>();

            foreach (var column in columns)
            {
                indexKeysDefinitions.Add(indexKeysBuilder.Ascending(column.AsString));
            }

            var indexKeys = Builders<BsonDocument>.IndexKeys.Combine(indexKeysDefinitions);

            var indexOptions = new CreateIndexOptions { Name = indexName, Unique = isUnique };
            collection?.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(indexKeys, indexOptions));

            _catalogManager?.CreateIndex(databaseName, tableName, indexName, columns.Select(column => column.AsString).ToList(), isUnique);
        }


        public static void DropIndex(string databaseName, string tableName, string indexName, IMongoClient _client, CatalogManager _catalogManager)
        {
            var collection = _client?.GetDatabase(databaseName).GetCollection<BsonDocument>(tableName);
            collection?.Indexes.DropOne(indexName);

            _catalogManager?.DropIndex(databaseName, tableName, indexName);
        }


        public static void DropDatabase(string databaseName, IMongoClient _client, CatalogManager _catalogManager)
        {
            Console.WriteLine($"Dropping database: {databaseName}");
            _client?.DropDatabase(databaseName);
            _catalogManager?.DropDatabase(databaseName);
        }

        public static void DropTable(string databaseName, string tableName, IMongoClient _client, CatalogManager _catalogManager)
        {
            Console.WriteLine($"Dropping table: {databaseName}.{tableName}");
            var database = _client.GetDatabase(databaseName);
            database.DropCollection(tableName);
            database.DropCollection(tableName + "_index");
            _catalogManager?.DropTable(databaseName, tableName);
        }

        public static void Insert(string databaseName, string tableName, Dictionary<string, object> row, IMongoClient _client, CatalogManager _catalogManager)
        {
            var collection = _client.GetDatabase(databaseName).GetCollection<BsonDocument>(tableName);

            if (CheckUnique(databaseName, tableName, row, _client, _catalogManager))
            {
                throw new Exception($"Insert failed. Unique index constraint violated in table '{tableName}' in database '{databaseName}'.");
            }

            // Insert data into the main collection
            var document = new BsonDocument();
            var idKey = row.Keys.First();
            document["_id"] = Convert.ToInt32(row[idKey]);
            var otherValues = row.Where(kvp => kvp.Key != idKey).Select(kvp => kvp.Value.ToString());
            document["value"] = string.Join("#", otherValues);
            collection.InsertOne(document);

            // Insert data into index collections
            var indexes = _catalogManager.GetIndexes(databaseName, tableName);
            foreach (var index in indexes)
            {
                // Skip if the index is the primary key, as MongoDB automatically creates an index for it
                if (index.Name == idKey)
                    continue;

                var indexCollection = _client.GetDatabase(databaseName).GetCollection<BsonDocument>(tableName + "_index");

                var indexDocument = new BsonDocument();
                indexDocument["_id"] = index.Name;

                var filter = Builders<BsonDocument>.Filter.Eq("_id", index.Name);

                var prevDoc = indexCollection.Find(filter).FirstOrDefault();

                var indexValues = index.Columns.Select(columnName => row[columnName].ToString());
                var indexValue = string.Join("&", indexValues);

                if (prevDoc == null)
                {
                    // This is the first document for this index
                    indexDocument["value"] = indexValue;
                }
                else
                {
                    // This is not the first document, concatenate the new values
                    indexDocument["value"] = prevDoc.GetValue("value") + "#" + indexValue;
                }


                var replaceFilter = Builders<BsonDocument>.Filter.Eq("_id", indexDocument["_id"]);
                indexCollection.ReplaceOne(replaceFilter, indexDocument, new ReplaceOptions { IsUpsert = true });
            }
        }



        public static bool CheckUnique(string databaseName, string tableName, Dictionary<string, object> row, IMongoClient _client, CatalogManager _catalogManager)
        {
            var indexes = _catalogManager.GetIndexes(databaseName, tableName);
            var indexCollection = _client.GetDatabase(databaseName).GetCollection<BsonDocument>(tableName + "_index");

            foreach (var index in indexes)
            {
                if (index.IsUnique)
                {
                    var filter = Builders<BsonDocument>.Filter.Eq("_id", index.Name);
                    var indexDocument = indexCollection.Find(filter).FirstOrDefault();
                    if (indexDocument != null)
                    {
                        var indexValues = index.Columns.Select(columnName => row[columnName].ToString());
                        var indexValue = string.Join("&", indexValues);
                        if (indexDocument.GetValue("value").AsString.Contains(indexValue))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    continue;
                }
                //var indexValues = index.Columns.Select(columnName => row[columnName].ToString());
                //var indexValue = string.Join("&", indexValues);
                //if (indexDocument.GetValue("value").AsString.Contains(indexValue))
                //{
                //    return true;
                //}
            }
            return false;
        }



        //public static void Insert(string databaseName, string tableName, Dictionary<string, object> row, IMongoClient _client, CatalogManager _catalogManager)
        //{
        //    var collection = _client.GetDatabase(databaseName).GetCollection<BsonDocument>(tableName);
        //    var indexCollection = _client.GetDatabase(databaseName).GetCollection<BsonDocument>(tableName + "_index");

        //    var idKey = row.Keys.First();
        //    var idValue = Convert.ToInt32(row[idKey]);

        //    var indexList = _catalogManager.GetIndexes(databaseName, tableName);

        //    foreach (var index in indexList)
        //    {
        //        foreach (var column in index.Columns)
        //        {
        //            if (!row.ContainsKey(column))
        //            {
        //                throw new ArgumentException($"Column '{column}' is part of index '{index.Name}' but it is not present in the row data.");
        //            }
        //        }
        //    }
        //    var filter = Builders<BsonDocument>.Filter.Eq("_id", Convert.ToInt32(row[indexList.FirstOrDefault(index => index.IsUnique).Columns[0]]));
        //    var existingDocument = collection.Find(filter);
        //    var frequency= new Dictionary<string, int>();

        //    foreach (var kvp in existingDocument.ToList())
        //    {

        //        var values=kvp.Values.ToList();
        //        foreach(var value in values)
        //        {
        //            if (frequency.ContainsKey(value.ToString()))
        //            {
        //                frequency[value.ToString()]++;
        //            }
        //            else
        //            {
        //                frequency[value.ToString()] = 1;
        //            }
        //        }
        //        if ()
        //        //if (existingDocument != null)
        //        //{
        //        //    throw new Exception($"Insert failed. Unique index constraint violated on column '{index.Columns[0]}' in table '{tableName}' in database '{databaseName}'.");
        //        //}
        //    }

        //    var document = new BsonDocument();
        //    var id = row.Keys.First();
        //    document["_id"] = Convert.ToInt32(row[id]);
        //    var otherValues = row.Where(kvp => kvp.Key != id).Select(kvp => kvp.Value.ToString());
        //    document["value"] = string.Join("#", otherValues);

        //    collection.InsertOne(document);
        //}

        public static void Delete(string databaseName, string tableName, FilterDefinition<BsonDocument> Filter, IMongoClient _client, CatalogManager _catalogManager)
        {
            var collection = _client.GetDatabase(databaseName).GetCollection<BsonDocument>(tableName);

            var foreignKeyReferences = _catalogManager.GetForeignKeyReferences(databaseName, tableName);
            foreach (var reference in foreignKeyReferences)
            {
                var refCollection = _client.GetDatabase(databaseName).GetCollection<BsonDocument>(reference.TableName);
                var existingDocument = refCollection.Find(Filter).FirstOrDefault();

                if (existingDocument != null)
                {
                    throw new Exception($"Delete failed. Foreign key constraint violated in table '{reference.TableName}' in database '{databaseName}' on column '{reference.ColumnName}'.");
                }
            }

            collection.DeleteOne(Filter);
        }



       
    }
}