-- Run manually on the Billing database before deploying demo payment support.
-- Repeatable: constraint names are preserved by this atomic ALTER. Does not mark existing rows Paid.
USE zumaris_billing;
ALTER TABLE payment_intents
 DROP CHECK ck_payment_gateway,
 DROP CHECK ck_payment_stage,
 ADD CONSTRAINT ck_payment_gateway CHECK(gateway IN ('PayPal','Demo')),
 ADD CONSTRAINT ck_payment_stage CHECK(
  (gateway='PayPal' AND status='AwaitingGateway') OR (gateway='Demo' AND status='Paid')
 );
