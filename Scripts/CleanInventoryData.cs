using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MarketBot.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MarketBot.Scripts
{
    /// <summary>
    /// Script to clean existing Redis inventory data before switching between dry-run and production modes.
    /// This prevents format conflicts and ensures clean state transitions between modes.
    /// 
    /// Use cases:
    /// - Before switching from dry-run to production mode
    /// - Before switching from production to dry-run mode  
    /// - When upgrading quantity format systems
    /// - When resolving Redis data corruption
    /// </summary>
    public class CleanInventoryDataScript
    {
        private readonly IDatabase _redisDatabase;
        private readonly ILogger<CleanInventoryDataScript> _logger;

        public CleanInventoryDataScript(IDatabase redisDatabase, ILogger<CleanInventoryDataScript> logger)
        {
            _redisDatabase = redisDatabase;
            _logger = logger;
        }

        /// <summary>
        /// Cleans all inventory data from Redis.
        /// This includes both global and market-specific inventories.
        /// </summary>
        public async Task CleanAllInventoryData()
        {
            _logger.LogInformation("Starting inventory data cleanup...");

            try
            {
                // Clean global inventory
                await CleanGlobalInventory();

                // Clean market-specific inventories (markets 1-5 as seen in InventoryService)
                for (ulong marketId = 1; marketId <= 5; marketId++)
                {
                    await CleanMarketInventory(marketId);
                    await CleanMarketWarehouseLots(marketId);
                }
                
                // Clean global warehouse lots
                await CleanGlobalWarehouseLots();

                _logger.LogInformation("Inventory data cleanup completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean inventory data");
                throw;
            }
        }

        /// <summary>
        /// Cleans the global inventory data.
        /// </summary>
        private async Task CleanGlobalInventory()
        {
            const string globalKey = "inventory:global:items";
            
            _logger.LogInformation("Cleaning global inventory data...");
            
            // Check if key exists before deletion
            bool exists = await _redisDatabase.KeyExistsAsync(globalKey);
            if (exists)
            {
                // Get count before deletion for logging
                long itemCount = await _redisDatabase.HashLengthAsync(globalKey);
                
                // Delete the entire hash
                bool deleted = await _redisDatabase.KeyDeleteAsync(globalKey);
                
                if (deleted)
                {
                    _logger.LogInformation("Deleted global inventory with {ItemCount} items", itemCount);
                }
                else
                {
                    _logger.LogWarning("Failed to delete global inventory key");
                }
            }
            else
            {
                _logger.LogInformation("Global inventory key does not exist, nothing to clean");
            }
        }

        /// <summary>
        /// Cleans inventory data for a specific market.
        /// </summary>
        /// <param name="marketId">The market ID to clean</param>
        private async Task CleanMarketInventory(ulong marketId)
        {
            string marketKey = $"inventory:{marketId}:items";
            
            _logger.LogInformation("Cleaning inventory data for market {MarketId}...", marketId);
            
            // Check if key exists before deletion
            bool exists = await _redisDatabase.KeyExistsAsync(marketKey);
            if (exists)
            {
                // Get count before deletion for logging
                long itemCount = await _redisDatabase.HashLengthAsync(marketKey);
                
                // Delete the entire hash
                bool deleted = await _redisDatabase.KeyDeleteAsync(marketKey);
                
                if (deleted)
                {
                    _logger.LogInformation("Deleted market {MarketId} inventory with {ItemCount} items", marketId, itemCount);
                }
                else
                {
                    _logger.LogWarning("Failed to delete inventory key for market {MarketId}", marketId);
                }
            }
            else
            {
                _logger.LogInformation("Market {MarketId} inventory key does not exist, nothing to clean", marketId);
            }
        }

        /// <summary>
        /// Safely cleans inventory data with backup option.
        /// Creates a backup before deletion and provides rollback capability.
        /// </summary>
        /// <param name="createBackup">Whether to create a backup before cleaning</param>
        public async Task SafeCleanWithBackup(bool createBackup = true)
        {
            if (createBackup)
            {
                await CreateInventoryBackup();
            }

            await CleanAllInventoryData();
        }

        /// <summary>
        /// Creates a backup of current inventory data before cleaning.
        /// </summary>
        private async Task CreateInventoryBackup()
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            
            _logger.LogInformation("Creating inventory backup with timestamp {Timestamp}...", timestamp);

            try
            {
                // Backup global inventory
                await BackupInventoryKey("inventory:global:items", $"backup:{timestamp}:inventory:global:items");

                // Backup market inventories
                for (ulong marketId = 1; marketId <= 5; marketId++)
                {
                    await BackupInventoryKey(
                        $"inventory:{marketId}:items", 
                        $"backup:{timestamp}:inventory:{marketId}:items");
                }

                _logger.LogInformation("Inventory backup created successfully with timestamp {Timestamp}", timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create inventory backup");
                throw;
            }
        }

        /// <summary>
        /// Backs up a single inventory key.
        /// </summary>
        private async Task BackupInventoryKey(string sourceKey, string backupKey)
        {
            bool exists = await _redisDatabase.KeyExistsAsync(sourceKey);
            if (exists)
            {
                // Get all hash entries
                var hashEntries = await _redisDatabase.HashGetAllAsync(sourceKey);
                
                if (hashEntries.Length > 0)
                {
                    // Store in backup key
                    await _redisDatabase.HashSetAsync(backupKey, hashEntries);
                    
                    // Set expiration for backup (30 days)
                    await _redisDatabase.KeyExpireAsync(backupKey, TimeSpan.FromDays(30));
                    
                    _logger.LogDebug("Backed up {Count} items from {SourceKey} to {BackupKey}", 
                        hashEntries.Length, sourceKey, backupKey);
                }
            }
        }

        /// <summary>
        /// Lists available inventory backups.
        /// </summary>
        public Task<string[]> ListAvailableBackups()
        {
            var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints()[0]);
            var backupKeys = server.Keys(pattern: "backup:*:inventory:*");
            
            var backups = new List<string>();
            foreach (var key in backupKeys)
            {
                backups.Add(key);
            }
            
            return Task.FromResult(backups.ToArray());
        }

        /// <summary>
        /// Restores inventory data from a specific backup.
        /// USE WITH CAUTION: This will overwrite current inventory data.
        /// </summary>
        /// <param name="backupTimestamp">The timestamp of the backup to restore</param>
        public async Task RestoreFromBackup(string backupTimestamp)
        {
            _logger.LogWarning("RESTORING INVENTORY DATA FROM BACKUP {BackupTimestamp} - THIS WILL OVERWRITE CURRENT DATA", backupTimestamp);

            try
            {
                // Restore global inventory
                await RestoreInventoryKey($"backup:{backupTimestamp}:inventory:global:items", "inventory:global:items");

                // Restore market inventories
                for (ulong marketId = 1; marketId <= 5; marketId++)
                {
                    await RestoreInventoryKey(
                        $"backup:{backupTimestamp}:inventory:{marketId}:items", 
                        $"inventory:{marketId}:items");
                }

                _logger.LogInformation("Successfully restored inventory data from backup {BackupTimestamp}", backupTimestamp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore inventory data from backup {BackupTimestamp}", backupTimestamp);
                throw;
            }
        }

        /// <summary>
        /// Cleans warehouse lots for a specific market.
        /// </summary>
        /// <param name="marketId">The market ID to clean</param>
        private async Task CleanMarketWarehouseLots(ulong marketId)
        {
            string lotKeyPattern = $"lots:{marketId}:*";
            
            _logger.LogInformation("Cleaning warehouse lots for market {MarketId}...", marketId);
            
            try
            {
                var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints()[0]);
                var lotKeys = server.Keys(pattern: lotKeyPattern);
                
                var deletedCount = 0;
                foreach (var key in lotKeys)
                {
                    if (await _redisDatabase.KeyDeleteAsync(key))
                    {
                        deletedCount++;
                    }
                }
                
                if (deletedCount > 0)
                {
                    _logger.LogInformation("Deleted {DeletedCount} warehouse lot keys for market {MarketId}", deletedCount, marketId);
                }
                else
                {
                    _logger.LogInformation("No warehouse lot keys found for market {MarketId}", marketId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean warehouse lots for market {MarketId}", marketId);
            }
        }
        
        /// <summary>
        /// Cleans global warehouse lots.
        /// </summary>
        private async Task CleanGlobalWarehouseLots()
        {
            string lotKeyPattern = "lots:global:*";
            
            _logger.LogInformation("Cleaning global warehouse lots...");
            
            try
            {
                var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints()[0]);
                var lotKeys = server.Keys(pattern: lotKeyPattern);
                
                var deletedCount = 0;
                foreach (var key in lotKeys)
                {
                    if (await _redisDatabase.KeyDeleteAsync(key))
                    {
                        deletedCount++;
                    }
                }
                
                if (deletedCount > 0)
                {
                    _logger.LogInformation("Deleted {DeletedCount} global warehouse lot keys", deletedCount);
                }
                else
                {
                    _logger.LogInformation("No global warehouse lot keys found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean global warehouse lots");
            }
        }
        
        /// <summary>
        /// Cleans all warehouse-related data including lots and summaries.
        /// </summary>
        public async Task CleanAllWarehouseData()
        {
            _logger.LogInformation("Starting comprehensive warehouse data cleanup...");
            
            try
            {
                // Clean existing inventory data
                await CleanAllInventoryData();
                
                // Clean warehouse lots
                await CleanAllWarehouseLots();
                
                // Clean warehouse summaries
                await CleanWarehouseSummaries();
                
                _logger.LogInformation("Comprehensive warehouse data cleanup completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean comprehensive warehouse data");
                throw;
            }
        }
        
        /// <summary>
        /// Cleans all warehouse lots across all markets.
        /// </summary>
        private async Task CleanAllWarehouseLots()
        {
            _logger.LogInformation("Cleaning all warehouse lots...");
            
            try
            {
                var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints()[0]);
                var allLotKeys = server.Keys(pattern: "lots:*");
                
                var deletedCount = 0;
                foreach (var key in allLotKeys)
                {
                    if (await _redisDatabase.KeyDeleteAsync(key))
                    {
                        deletedCount++;
                    }
                }
                
                if (deletedCount > 0)
                {
                    _logger.LogInformation("Deleted {DeletedCount} total warehouse lot keys", deletedCount);
                }
                else
                {
                    _logger.LogInformation("No warehouse lot keys found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean all warehouse lots");
            }
        }
        
        /// <summary>
        /// Cleans warehouse summaries.
        /// </summary>
        private async Task CleanWarehouseSummaries()
        {
            _logger.LogInformation("Cleaning warehouse summaries...");
            
            try
            {
                var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints()[0]);
                var summaryKeys = server.Keys(pattern: "warehouse:summary:*");
                
                var deletedCount = 0;
                foreach (var key in summaryKeys)
                {
                    if (await _redisDatabase.KeyDeleteAsync(key))
                    {
                        deletedCount++;
                    }
                }
                
                if (deletedCount > 0)
                {
                    _logger.LogInformation("Deleted {DeletedCount} warehouse summary keys", deletedCount);
                }
                else
                {
                    _logger.LogInformation("No warehouse summary keys found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean warehouse summaries");
            }
        }

        /// <summary>
        /// Restores a single inventory key from backup.
        /// </summary>
        private async Task RestoreInventoryKey(string backupKey, string targetKey)
        {
            bool exists = await _redisDatabase.KeyExistsAsync(backupKey);
            if (exists)
            {
                // Get all backup data
                var hashEntries = await _redisDatabase.HashGetAllAsync(backupKey);
                
                if (hashEntries.Length > 0)
                {
                    // Clear existing data
                    await _redisDatabase.KeyDeleteAsync(targetKey);
                    
                    // Restore data
                    await _redisDatabase.HashSetAsync(targetKey, hashEntries);
                    
                    _logger.LogDebug("Restored {Count} items from {BackupKey} to {TargetKey}", 
                        hashEntries.Length, backupKey, targetKey);
                }
            }
            else
            {
                _logger.LogWarning("Backup key {BackupKey} does not exist", backupKey);
            }
        }
    }
}