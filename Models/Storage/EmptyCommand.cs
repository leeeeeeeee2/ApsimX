﻿namespace Models.Storage
{
    using APSIM.Shared.Utilities;
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;

    /// <summary>Encapsulates a command to empty the database as much as possible.</summary>
    class EmptyCommand : IRunnable
    {
        private IDatabaseConnection database;

        /// <summary>Constructor</summary>
        /// <param name="databaseConnection">The database to cleanup.</param>
        public EmptyCommand(IDatabaseConnection databaseConnection)
        {
            database = databaseConnection;
        }

        /// <summary>Called to run the command. Can throw on error.</summary>
        /// <param name="cancelToken">Is cancellation pending?</param>
        public void Run(CancellationTokenSource cancelToken)
        {
            var checkpointData = new DataView(database.ExecuteQuery("SELECT * FROM _Checkpoints"));
            checkpointData.RowFilter = "Name='Current'";
            if (checkpointData.Count == 1)
            {
                int checkId = Convert.ToInt32(checkpointData[0]["ID"]);

                // Delete current data from all tables.
                foreach (string tableName in database.GetTableNames())
                {
                    if (!tableName.StartsWith("_"))
                        database.ExecuteNonQuery(string.Format("DELETE FROM {0} WHERE CheckpointID={1}", tableName, checkId));
                }

                // Delete empty tables.
                List<string> tableNamesToDelete = new List<string>();
                bool allTablesEmpty = true;
                foreach (string tableName in database.GetTableNames())
                {
                    if (!tableName.StartsWith("_"))
                    {
                        var data = database.ExecuteQuery(string.Format("SELECT * FROM {0} LIMIT 1", tableName));
                        if (data.Rows.Count == 0)
                            tableNamesToDelete.Add(tableName);
                        else
                            allTablesEmpty = false;
                    }
                }

                // If all data tables were emptied then delete all tables.
                if (allTablesEmpty)
                    tableNamesToDelete = database.GetTableNames();

                foreach (string tableName in tableNamesToDelete)
                    database.ExecuteNonQuery(string.Format("DROP TABLE {0}", tableName));
            }
        }
    }
}