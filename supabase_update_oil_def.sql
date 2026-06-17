-- 1. Create Tables

CREATE TABLE IF NOT EXISTS "ProductMasters" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "ProductName" VARCHAR(200) NOT NULL,
    "Category" VARCHAR(10) NOT NULL DEFAULT 'Oil',
    "Unit" VARCHAR(50) NOT NULL DEFAULT 'Litre',
    "DefaultSaleRate" DOUBLE PRECISION NOT NULL,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid")
);

CREATE TABLE IF NOT EXISTS "OilDefInventories" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "Year" INTEGER NOT NULL,
    "Month" INTEGER NOT NULL,
    "ProductType" VARCHAR(10) NOT NULL DEFAULT 'Oil',
    "ProductId" UUID NOT NULL,
    "OpeningStock" DOUBLE PRECISION NOT NULL,
    "ClosingStock" DOUBLE PRECISION NOT NULL,
    "SalePrice" DOUBLE PRECISION NOT NULL,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_oil_def_inventories_product FOREIGN KEY ("ProductId") REFERENCES "ProductMasters" ("SyncGuid") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "OilDefPurchases" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "ProductType" VARCHAR(10) NOT NULL DEFAULT 'Oil',
    "ProductId" UUID NOT NULL,
    "SupplierName" VARCHAR(200) NOT NULL,
    "InvoiceNumber" VARCHAR(100) NOT NULL,
    "PurchaseDate" TIMESTAMP NOT NULL,
    "Quantity" DOUBLE PRECISION NOT NULL,
    "UnitPrice" DOUBLE PRECISION NOT NULL,
    "TotalCost" DOUBLE PRECISION NOT NULL,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_oil_def_purchases_product FOREIGN KEY ("ProductId") REFERENCES "ProductMasters" ("SyncGuid") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "OilDefDailyLogs" (
    "SyncGuid" UUID NOT NULL DEFAULT gen_random_uuid(),
    "station_id" TEXT NOT NULL,
    "local_id" INTEGER NOT NULL,
    "machine_id" TEXT,
    "LogDate" TIMESTAMP NOT NULL,
    "ProductType" VARCHAR(10) NOT NULL DEFAULT 'Oil',
    "ProductId" UUID NOT NULL,
    "OverrideSaleRate" DOUBLE PRECISION,
    "AddedQuantity" DOUBLE PRECISION NOT NULL,
    "SoldQuantity" DOUBLE PRECISION NOT NULL,
    "RemainingStock" DOUBLE PRECISION NOT NULL,
    "AdjustmentQuantity" DOUBLE PRECISION NOT NULL,
    "AdjustmentType" VARCHAR(100),
    "Remarks" TEXT,
    "created_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    "updated_at" TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    PRIMARY KEY ("SyncGuid"),
    CONSTRAINT fk_oil_def_daily_logs_product FOREIGN KEY ("ProductId") REFERENCES "ProductMasters" ("SyncGuid") ON DELETE CASCADE
);

-- 2. Setup Triggers for Automatic updated_at Update
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ language 'plpgsql';

CREATE TRIGGER tr_update_timestamp BEFORE UPDATE ON "ProductMasters" FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
CREATE TRIGGER tr_update_timestamp BEFORE UPDATE ON "OilDefInventories" FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
CREATE TRIGGER tr_update_timestamp BEFORE UPDATE ON "OilDefPurchases" FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
CREATE TRIGGER tr_update_timestamp BEFORE UPDATE ON "OilDefDailyLogs" FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- 3. Enable Row Level Security (RLS)
ALTER TABLE "ProductMasters" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "OilDefInventories" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "OilDefPurchases" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "OilDefDailyLogs" ENABLE ROW LEVEL SECURITY;

-- 4. Define Row Level Security Policies
CREATE POLICY all_by_station ON "ProductMasters" FOR ALL USING (true) WITH CHECK (true);
CREATE POLICY all_by_station ON "OilDefInventories" FOR ALL USING (true) WITH CHECK (true);
CREATE POLICY all_by_station ON "OilDefPurchases" FOR ALL USING (true) WITH CHECK (true);
CREATE POLICY all_by_station ON "OilDefDailyLogs" FOR ALL USING (true) WITH CHECK (true);

-- 5. Recommended Optimization Indexes
CREATE INDEX IF NOT EXISTS idx_product_masters_sync ON "ProductMasters" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_oil_def_inventories_sync ON "OilDefInventories" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_oil_def_purchases_sync ON "OilDefPurchases" ("station_id", "updated_at");
CREATE INDEX IF NOT EXISTS idx_oil_def_daily_logs_sync ON "OilDefDailyLogs" ("station_id", "updated_at");

CREATE INDEX IF NOT EXISTS idx_oil_def_inventories_product ON "OilDefInventories" ("ProductId");
CREATE INDEX IF NOT EXISTS idx_oil_def_purchases_product ON "OilDefPurchases" ("ProductId");
CREATE INDEX IF NOT EXISTS idx_oil_def_daily_logs_product ON "OilDefDailyLogs" ("ProductId");
