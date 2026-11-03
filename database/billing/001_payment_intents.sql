-- Run manually as administrator. No gateway charge or settlement is performed.
CREATE DATABASE IF NOT EXISTS zumaris_billing CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE zumaris_billing;
CREATE TABLE IF NOT EXISTS payment_intents (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 request_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 quote_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 customer_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 amount DECIMAL(10,2) NOT NULL,
 currency_code CHAR(3) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 gateway VARCHAR(20) NOT NULL,
 status VARCHAR(30) NOT NULL,
 created_at DATETIME(6) NOT NULL,
 UNIQUE KEY uq_payment_request(request_id),
 INDEX ix_payment_customer(customer_id,created_at,id),
 CONSTRAINT ck_payment_amount CHECK(amount>0 AND amount<=999999.99),
 CONSTRAINT ck_payment_currency CHECK(currency_code='GBP'),
 CONSTRAINT ck_payment_gateway CHECK(gateway='PayPal'),
 CONSTRAINT ck_payment_stage CHECK(status='AwaitingGateway')
) ENGINE=InnoDB;
-- No cross-service foreign keys. Completed job and accepted quote are verified via API.
-- No endpoint can mark payment Paid; trusted PayPal integration is explicitly deferred.
