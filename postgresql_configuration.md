# PostgreSQL Configuration for Two-Phase Commit

This document outlines the necessary PostgreSQL configuration settings to enable the use of prepared transactions for our Two-Phase Commit (2PC) implementation.

## Required Settings

1. **max_prepared_transactions** - This parameter must be set to a non-zero value to enable prepared transactions.

```
# Default value is 0, which disables prepared transactions
max_prepared_transactions = 100  # Recommended value
```

## Configuration Steps

### For Local Development

1. Edit your `postgresql.conf` file:
   ```
   # Find the file location with: SHOW config_file;
   sudo nano /path/to/postgresql.conf
   ```

2. Set the `max_prepared_transactions` parameter:
   ```
   max_prepared_transactions = 100
   ```

3. Restart PostgreSQL:
   ```
   sudo systemctl restart postgresql
   ```

### For Amazon Aurora PostgreSQL

1. Create a custom parameter group in the RDS console.

2. Update the `max_prepared_transactions` parameter to a non-zero value.

3. Apply the parameter group to your Aurora PostgreSQL cluster.

4. Reboot the cluster to apply the changes.

## Verification

To verify that prepared transactions are enabled:

```sql
SHOW max_prepared_transactions;
```

The result should be a non-zero value.

## Important Notes

1. Prepared transactions will remain in memory until they are explicitly committed or rolled back.

2. For production systems, set up monitoring for "orphaned" prepared transactions.

3. Use the following query to check for prepared transactions that may need cleanup:

```sql
SELECT * FROM pg_prepared_xacts;
```

4. To manually clean up a prepared transaction:

```sql
ROLLBACK PREPARED 'transaction_id';
```

## Monitoring

To monitor PostgreSQL prepared transactions, add these metrics to your monitoring system:

- Number of prepared transactions
- Age of oldest prepared transaction
- Number of transactions approaching the timeout limit

## Troubleshooting

If you encounter errors related to prepared transactions:

1. Verify that `max_prepared_transactions` is set correctly.
2. Ensure that the database has enough memory to handle the number of prepared transactions.
3. Check for orphaned prepared transactions that might be consuming resources.
4. Verify that all microservices are properly committing or rolling back their prepared transactions. 