# Billing

Authenticated, customer-owned fixed-price payment intents for completed ServiceRequest jobs. Production port 5008; Development port 5112. See docs/technician-order-workflow.md for manual SQL, initialization, Gateway and IIS deployment.

Only the overall PayPal payment stage is in scope. Records remain AwaitingGateway, canPay=false, checkoutUrl=null. No charge, fake Paid status, webhook, settlement, commission, refund or ticket workflow is implemented. A real gateway adapter is required before customers can pay.
