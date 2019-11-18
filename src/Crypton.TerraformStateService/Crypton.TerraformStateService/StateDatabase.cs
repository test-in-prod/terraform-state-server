using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading.Tasks;

namespace Crypton.TerraformStateService
{
    public class StateDatabase : IDisposable
    {

        readonly SQLiteConnection dbConn = null;

        public StateDatabase(string databaseFilePath)
        {
            dbConn = new SQLiteConnection($"Data Source={databaseFilePath};Version=3;FailIfMissing=True;Foreign Keys=True;");
            dbConn.Open();
        }

        public SQLiteConnection Connection
        {
            get
            {
                return dbConn;
            }
        }

        /// <summary>
        /// Gets the state contained in the database, returning null if state does not exist
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public string GetState(string name)
        {
            if (!(new StateNameValidator()).IsValid(name))
                throw new ArgumentException(nameof(name));
            using (var cmd = dbConn.CreateCommand())
            {
                cmd.CommandText = @"SELECT data FROM states WHERE name = @name";
                cmd.Parameters.AddWithValue("@name", name);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return reader["data"] as string;
                    }
                    else
                    {
                        return null;
                    }
                }
            }
        }

        /// <summary>
        /// Tests if the state is locked in a transaction context
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="name"></param>
        /// <param name="lockId"></param>
        /// <param name="lockData"></param>
        /// <returns></returns>
        private bool isLocked(SQLiteTransaction transaction, string name, out string lockId, out string lockData)
        {
            using (var cmd = dbConn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT locked, lock_id, lock_data FROM states WHERE name = @name";
                cmd.Parameters.AddWithValue("@name", name);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        // found state, let's see if it's locked
                        var locked = (bool)reader["locked"];
                        lockId = reader["lock_id"] as string;
                        lockData = reader["lock_data"] as string;
                        return locked;
                    }
                    else
                    {
                        // state does not exist
                        lockId = null;
                        lockData = null;
                        return false;
                    }
                }
            }
        }

        public enum StateUpdateResult
        {
            /// <summary>
            /// State is locked by something else
            /// </summary>
            LockedOut,

            /// <summary>
            /// State has been updated
            /// </summary>
            Success
        }

        /// <summary>
        /// Deletes specified state
        /// </summary>
        /// <param name="name"></param>
        /// <param name="force"></param>
        public void DeleteState(string name, bool force = false)
        {
            using (var trx = dbConn.BeginTransaction())
            {
                try
                {
                    if (isLocked(trx, name, out string lockId, out string lockData) && !force)
                    {
                        throw new StateLockedException("State is locked", lockData);
                    }

                    using (var cmd = dbConn.CreateCommand())
                    {
                        cmd.Transaction = trx;
                        cmd.CommandText = "DELETE FROM states WHERE name = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        cmd.ExecuteNonQuery();
                        trx.Commit();
                    }
                }
                catch
                {
                    trx.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// Performs a transactional state unlock based on the lock request
        /// </summary>
        /// <param name="name"></param>
        /// <param name="lockRequest"></param>
        public void TransactionalUnlock(string name, LockRequest lockRequest)
        {
            using (var trx = dbConn.BeginTransaction())
            {
                try
                {
                    if (isLocked(trx, name, out string lockId, out string lockData))
                    {
                        if (lockId == lockRequest.ID)
                        {
                            // unlock
                            using (var cmd = dbConn.CreateCommand())
                            {
                                cmd.Transaction = trx;
                                cmd.CommandText = "UPDATE states SET locked=0, lock_id = NULL, lock_data = NULL WHERE name = @name";
                                cmd.Parameters.AddWithValue("@name", name);
                                cmd.ExecuteNonQuery();
                                trx.Commit();
                            }
                        }
                        else
                        {
                            // invalid lock id for the request
                            throw new StateLockedException("State is locked by another ID", lockData);
                        }
                    }
                    else
                    {
                        // state is not locked, something is wrong
                        throw new InvalidOperationException("State is not locked");
                    }
                }
                catch
                {
                    trx.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// Performs a transactional state lock based on parameters given
        /// </summary>
        /// <param name="name"></param>
        /// <param name="lockRequest"></param>
        public void TransactionalLock(string name, LockRequest lockRequest)
        {
            using (var trx = dbConn.BeginTransaction())
            {
                try
                {
                    if (isLocked(trx, name, out string lockId, out string lockData))
                    {
                        throw new StateLockedException("State is already locked", lockData);
                    }
                    else
                    {
                        lockData = JsonConvert.SerializeObject(lockRequest);

                        // try updating first
                        int affected;
                        using (var cmd = dbConn.CreateCommand())
                        {
                            cmd.Transaction = trx;
                            cmd.CommandText = "UPDATE states SET locked=1, lock_id=@lock_id, lock_data=@lock_data WHERE name = @name";
                            cmd.Parameters.AddWithValue("@lock_id", lockRequest.ID);
                            cmd.Parameters.AddWithValue("@lock_data", lockData);
                            cmd.Parameters.AddWithValue("@name", name);
                            affected = cmd.ExecuteNonQuery();
                        }

                        if (affected > 0)
                        {
                            trx.Commit();
                        }
                        else
                        {
                            // state does not exist but a lock can be requested when creating a new state
                            using (var cmd = dbConn.CreateCommand())
                            {
                                cmd.Transaction = trx;
                                cmd.CommandText = "INSERT INTO states (name, locked, lock_id, lock_data) VALUES (@name, 1, @lock_id, @lock_data)";
                                cmd.Parameters.AddWithValue("@lock_id", lockRequest.ID);
                                cmd.Parameters.AddWithValue("@lock_data", lockData);
                                cmd.Parameters.AddWithValue("@name", name);
                                cmd.ExecuteNonQuery();
                                trx.Commit();
                            }
                        }
                    }
                }
                catch
                {
                    trx.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// Performs a transactional state update, checking for locks if any
        /// </summary>
        /// <param name="name">Name of state to update</param>
        /// <param name="state">State JSON document</param>
        /// <param name="lockId">Lock ID must be provided to update a locked state</param>
        public void TransactionalUpdate(string name, string state, string lockId = null)
        {
            using (var trx = dbConn.BeginTransaction())
            {
                try
                {
                    if (isLocked(trx, name, out string dbLockId, out string lockData))
                    {
                        if (lockId != dbLockId)
                        {
                            // current state is locked by someone else
                            throw new StateLockedException("State is locked out", lockData);
                        }
                    }

                    // try updating first
                    int affected;
                    using (var cmd = dbConn.CreateCommand())
                    {
                        cmd.Transaction = trx;
                        cmd.CommandText = "UPDATE states SET data = @data WHERE name = @name";
                        cmd.Parameters.AddWithValue("@name", name);
                        cmd.Parameters.AddWithValue("@data", state);
                        affected = cmd.ExecuteNonQuery();
                    }

                    if (affected > 0)
                    {
                        trx.Commit();
                    }
                    else
                    {
                        // state does not exist, create it
                        using (var cmd = dbConn.CreateCommand())
                        {
                            cmd.Transaction = trx;
                            cmd.CommandText = "INSERT INTO states (name, locked, data) VALUES (@name, 0, @data)";
                            cmd.Parameters.AddWithValue("@name", name);
                            cmd.Parameters.AddWithValue("@data", state);
                            cmd.ExecuteNonQuery();
                            trx.Commit();
                        }
                    }
                }
                catch
                {
                    trx.Rollback();
                    throw;
                }
            }
        }

        public void Dispose()
        {
            dbConn.Dispose();
        }
    }
}
