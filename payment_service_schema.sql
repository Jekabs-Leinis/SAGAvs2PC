CREATE TABLE "Payments" (
    "PaymentID" SERIAL PRIMARY KEY,
    "OrderID" INTEGER NOT NULL,
    "Amount" DECIMAL(10, 2) NOT NULL,
    "PaymentStatus" VARCHAR(50) NOT NULL DEFAULT 'PENDING',
    FOREIGN KEY ("OrderID") REFERENCES "Orders" ("OrderID")
); 