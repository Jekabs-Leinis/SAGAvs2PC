CREATE TABLE "Inventory"
(
    "ProductID"        SERIAL PRIMARY KEY,
    "StockQuantity"    INTEGER     NOT NULL DEFAULT 0,
    "ReservedQuantity" INTEGER     NOT NULL DEFAULT 0,
    "Status"           VARCHAR(50) NOT NULL DEFAULT 'NONE'
);

CREATE TABLE "Orders"
(
    "OrderID"     SERIAL PRIMARY KEY,
    "UserID"      INTEGER        NOT NULL,
    "ProductID"   INTEGER        NOT NULL,
    "Quantity"    INTEGER        NOT NULL,
    "Amount"      DECIMAL(10, 2) NOT NULL,
    "OrderStatus" VARCHAR(50)    NOT NULL DEFAULT 'PENDING'
);

CREATE TABLE "Payments"
(
    "PaymentID"     SERIAL PRIMARY KEY,
    "OrderID"       INTEGER        NOT NULL,
    "Amount"        DECIMAL(10, 2) NOT NULL,
    "PaymentStatus" VARCHAR(50)    NOT NULL DEFAULT 'PENDING',
    FOREIGN KEY ("OrderID") REFERENCES "Orders" ("OrderID")
);

DO
$$
BEGIN
FOR i IN 1..100 LOOP
        INSERT INTO "Inventory" ("StockQuantity", "ReservedQuantity", "Status")
SELECT FLOOR(50 + RANDOM() * 201), -- StockQuantity between 50 and 250
       FLOOR(RANDOM() * 51),       -- ReservedQuantity between 0 and 50
       'NONE'                      -- Example status
FROM generate_series(1, 10000); -- Generate 10,000 rows per batch
END LOOP;
END $$;