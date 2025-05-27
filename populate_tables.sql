-- Increase recursion depth for CTEs to allow for 1 million rows
SET max_recursive_iterations = 1005000; -- A bit more than 1M just in case

-- Disable foreign key checks for faster inserts
SET session_replication_role = 'replica';

-- 1. Populate Inventory Table (1 Million Products)
DO $$
BEGIN
    FOR i IN 1..100 LOOP
        INSERT INTO "Inventory" ("StockQuantity", "ReservedQuantity", "Status")
        SELECT
            FLOOR(50 + RANDOM() * 201),       -- StockQuantity between 50 and 250
            FLOOR(RANDOM() * 51),             -- ReservedQuantity between 0 and 50
            'NONE'                            -- Example status
        FROM generate_series(1, 10000);       -- Generate 10,000 rows per batch
    END LOOP;
END $$;

-- 2. Populate Orders Table (1 Million Orders)
DO $$
BEGIN
    FOR i IN 1..100 LOOP
        INSERT INTO Orders (UserID, ProductID, Quantity, Amount, OrderStatus)
        SELECT
            FLOOR(1 + RANDOM() * 100000),     -- UserID between 1 and 100,000
            FLOOR(1 + RANDOM() * 1000000),    -- ProductID between 1 and 1,000,000
            FLOOR(1 + RANDOM() * 5),          -- Quantity between 1 and 5
            ROUND(10 + RANDOM() * 490, 2),    -- Amount between 10.00 and 500.00
            CASE FLOOR(1 + RANDOM() * 5)      -- Random OrderStatus
                WHEN 1 THEN 'PENDING'
                WHEN 2 THEN 'CONFIRMED'
                WHEN 3 THEN 'CANCELLED'
                WHEN 4 THEN 'PAID'
                ELSE 'SHIPPED'
            END
        FROM generate_series(1, 10000);       -- Generate 10,000 rows per batch
    END LOOP;
END $$;

-- 3. Populate Payments Table (1 Million Payments)
DO $$
BEGIN
    FOR i IN 1..100 LOOP
        INSERT INTO Payments (OrderID, Amount, PaymentStatus)
        SELECT
            FLOOR(1 + RANDOM() * 1000000),    -- OrderID between 1 and 1,000,000
            ROUND(10 + RANDOM() * 490, 2),   -- Amount (random for simplicity)
            CASE FLOOR(1 + RANDOM() * 3)     -- Random PaymentStatus
                WHEN 1 THEN 'PENDING'
                WHEN 2 THEN 'SUCCESSFUL'
                ELSE 'FAILED'
            END
        FROM generate_series(1, 10000);       -- Generate 10,000 rows per batch
    END LOOP;
END $$;

-- Re-enable foreign key checks
SET session_replication_role = 'origin';

-- Reset recursion depth if desired (though it's a session variable)
-- SET max_recursive_iterations = DEFAULT;

SELECT 'Data population queries generated. Check table counts.';
SELECT COUNT(*) AS InventoryCount FROM Inventory;
SELECT COUNT(*) AS OrdersCount FROM Orders;
SELECT COUNT(*) AS PaymentsCount FROM Payments;
