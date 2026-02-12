using System.Text.RegularExpressions;
using PubSubApp.Models;

namespace PubSubApp.Validation;

// ORAE v2.0.0 Compliance Validation
// Validates incoming RetailEvent against ORAE v2.0.0 schema required fields
// Schema: https://transactiontree.com/schemas/orae/orae-2-0-0.json
public static class OraeValidator
{
    public static List<string> ValidateOraeCompliance(RetailEvent retailEvent)
    {
        var errors = new List<string>();

        // ── Required root fields (schema: required[]) ──
        if (string.IsNullOrEmpty(retailEvent.SchemaVersion))
            errors.Add("Missing required field: schemaVersion");
        else if (retailEvent.SchemaVersion != "2.0.0")
            errors.Add($"Invalid schemaVersion: {retailEvent.SchemaVersion}. Expected '2.0.0'");

        if (string.IsNullOrEmpty(retailEvent.MessageType))
            errors.Add("Missing required field: messageType");
        else if (retailEvent.MessageType != "RetailEvent")
            errors.Add($"Invalid messageType: {retailEvent.MessageType}. Expected 'RetailEvent'");

        if (string.IsNullOrEmpty(retailEvent.EventType))
            errors.Add("Missing required field: eventType");
        else if (!IsValidEnum(retailEvent.EventType, new[] { "ORIGINAL", "CORRECTION", "CANCELLATION", "SNAPSHOT" }))
            errors.Add($"Invalid eventType: {retailEvent.EventType}. Must be one of: ORIGINAL, CORRECTION, CANCELLATION, SNAPSHOT");

        if (string.IsNullOrEmpty(retailEvent.EventCategory))
            errors.Add("Missing required field: eventCategory");
        else if (!IsValidEnum(retailEvent.EventCategory, new[] {
            "TRANSACTION", "TILL", "STORE", "SESSION", "BANKING", "ORDER",
            "INVENTORY", "MAINTENANCE", "CALIBRATION", "AUDIT", "STATUS",
            "LOYALTY", "TOPUP", "PROMOTION", "OTHER" }))
            errors.Add($"Invalid eventCategory: {retailEvent.EventCategory}");

        if (string.IsNullOrEmpty(retailEvent.EventId))
            errors.Add("Missing required field: eventId");

        if (retailEvent.OccurredAt == default(DateTime))
            errors.Add("Missing required field: occurredAt");

        if (retailEvent.IngestedAt == default(DateTime))
            errors.Add("Missing required field: ingestedAt");

        // ── businessContext (schema: required) ──
        if (retailEvent.BusinessContext == null)
        {
            errors.Add("Missing required object: businessContext");
        }
        else
        {
            // businessContext.businessDay (required, format: date)
            if (retailEvent.BusinessContext.BusinessDay == default(DateTime))
                errors.Add("Missing required field: businessContext.businessDay");

            // businessContext.store (required)
            if (retailEvent.BusinessContext.Store == null)
                errors.Add("Missing required object: businessContext.store");
            else
            {
                // store.storeId (required)
                if (string.IsNullOrEmpty(retailEvent.BusinessContext.Store.StoreId))
                    errors.Add("Missing required field: businessContext.store.storeId");

                // store.currency format validation (optional but if present must be 3 uppercase letters)
                if (!string.IsNullOrEmpty(retailEvent.BusinessContext.Store.Currency) &&
                    !Regex.IsMatch(retailEvent.BusinessContext.Store.Currency, @"^[A-Z]{3}$"))
                    errors.Add($"Invalid businessContext.store.currency: '{retailEvent.BusinessContext.Store.Currency}'. Must be ISO-4217 (3 uppercase letters)");
            }

            // businessContext.workstation (required)
            if (retailEvent.BusinessContext.Workstation == null)
                errors.Add("Missing required object: businessContext.workstation");
            else
            {
                // workstation.registerId (required)
                if (string.IsNullOrEmpty(retailEvent.BusinessContext.Workstation.RegisterId))
                    errors.Add("Missing required field: businessContext.workstation.registerId");

                // workstation.sequenceNumber (optional but must be >= 0 if present)
                if (retailEvent.BusinessContext.Workstation.SequenceNumber.HasValue &&
                    retailEvent.BusinessContext.Workstation.SequenceNumber.Value < 0)
                    errors.Add("Invalid businessContext.workstation.sequenceNumber: must be >= 0");
            }

            // businessContext.channel (required)
            if (string.IsNullOrEmpty(retailEvent.BusinessContext.Channel))
                errors.Add("Missing required field: businessContext.channel");
        }

        // ── Category-specific payload validation (schema: anyOf) ──
        // At least one typed payload object must be present
        if (retailEvent.EventCategory == "TRANSACTION" && retailEvent.Transaction == null)
            errors.Add("eventCategory 'TRANSACTION' requires transaction object");

        // ── transaction (schema: $defs/Transaction) ──
        if (retailEvent.Transaction != null)
        {
            // transaction.transactionType (required, enum)
            if (string.IsNullOrEmpty(retailEvent.Transaction.TransactionType))
                errors.Add("Missing required field: transaction.transactionType");
            else if (!IsValidEnum(retailEvent.Transaction.TransactionType, new[] {
                "SALE", "RETURN", "EXCHANGE", "VOID", "CANCEL", "ADJUSTMENT", "NONMERCH", "SERVICE" }))
                errors.Add($"Invalid transaction.transactionType: {retailEvent.Transaction.TransactionType}. Must be one of: SALE, RETURN, EXCHANGE, VOID, CANCEL, ADJUSTMENT, NONMERCH, SERVICE");

            // transaction.totals (required)
            if (retailEvent.Transaction.Totals == null)
                errors.Add("Missing required object: transaction.totals");
            else
            {
                // totals.gross (required Money)
                if (retailEvent.Transaction.Totals.Gross == null)
                    errors.Add("Missing required field: transaction.totals.gross");
                else
                    ValidateMoney(retailEvent.Transaction.Totals.Gross, "transaction.totals.gross", errors);

                // totals.discounts (required Money)
                if (retailEvent.Transaction.Totals.Discounts == null)
                    errors.Add("Missing required field: transaction.totals.discounts");
                else
                    ValidateMoney(retailEvent.Transaction.Totals.Discounts, "transaction.totals.discounts", errors);

                // totals.tax (required Money)
                if (retailEvent.Transaction.Totals.Tax == null)
                    errors.Add("Missing required field: transaction.totals.tax");
                else
                    ValidateMoney(retailEvent.Transaction.Totals.Tax, "transaction.totals.tax", errors);

                // totals.net (required Money)
                if (retailEvent.Transaction.Totals.Net == null)
                    errors.Add("Missing required field: transaction.totals.net");
                else
                    ValidateMoney(retailEvent.Transaction.Totals.Net, "transaction.totals.net", errors);

                // totals.changeDue (optional Money)
                if (retailEvent.Transaction.Totals.ChangeDue != null)
                    ValidateMoney(retailEvent.Transaction.Totals.ChangeDue, "transaction.totals.changeDue", errors);

                // totals.cashRounding (optional Money)
                if (retailEvent.Transaction.Totals.CashRounding != null)
                    ValidateMoney(retailEvent.Transaction.Totals.CashRounding, "transaction.totals.cashRounding", errors);
            }

            // Items validation: must have at least 1 unless CANCEL/VOID (schema: allOf conditional)
            if (retailEvent.Transaction.TransactionType != "CANCEL" &&
                retailEvent.Transaction.TransactionType != "VOID" &&
                (retailEvent.Transaction.Items == null || retailEvent.Transaction.Items.Count == 0))
            {
                errors.Add("transaction.items[] must have at least one item for non-CANCEL/VOID transactions");
            }

            // ── LineItem validation (schema: $defs/LineItem) ──
            if (retailEvent.Transaction.Items != null)
            {
                for (int i = 0; i < retailEvent.Transaction.Items.Count; i++)
                {
                    var item = retailEvent.Transaction.Items[i];
                    string prefix = $"transaction.items[{i}]";

                    // lineId (required)
                    if (string.IsNullOrEmpty(item.LineId))
                        errors.Add($"Missing required field: {prefix}.lineId");

                    // item (required object with required sku + description)
                    if (item.Item == null)
                        errors.Add($"Missing required object: {prefix}.item");
                    else
                    {
                        if (string.IsNullOrEmpty(item.Item.Sku))
                            errors.Add($"Missing required field: {prefix}.item.sku");
                        if (string.IsNullOrEmpty(item.Item.Description))
                            errors.Add($"Missing required field: {prefix}.item.description");
                    }

                    // quantity (required object with required value + uom)
                    if (item.Quantity == null)
                        errors.Add($"Missing required object: {prefix}.quantity");
                    else
                    {
                        if (string.IsNullOrEmpty(item.Quantity.Uom))
                            errors.Add($"Missing required field: {prefix}.quantity.uom");
                    }

                    // pricing (required object with required unitPrice + extendedPrice)
                    if (item.Pricing == null)
                        errors.Add($"Missing required object: {prefix}.pricing");
                    else
                    {
                        if (item.Pricing.UnitPrice == null)
                            errors.Add($"Missing required field: {prefix}.pricing.unitPrice");
                        else
                            ValidateMoney(item.Pricing.UnitPrice, $"{prefix}.pricing.unitPrice", errors);

                        if (item.Pricing.ExtendedPrice == null)
                            errors.Add($"Missing required field: {prefix}.pricing.extendedPrice");
                        else
                            ValidateMoney(item.Pricing.ExtendedPrice, $"{prefix}.pricing.extendedPrice", errors);
                    }

                    // taxes[] - each TaxComponent requires amount (schema: $defs/TaxComponent)
                    if (item.Taxes != null)
                    {
                        for (int t = 0; t < item.Taxes.Count; t++)
                        {
                            var tax = item.Taxes[t];
                            if (tax.TaxAmount == null)
                                errors.Add($"Missing required field: {prefix}.taxes[{t}].amount");
                            else
                                ValidateMoney(tax.TaxAmount, $"{prefix}.taxes[{t}].amount", errors);
                        }
                    }
                }
            }

            // ── Tender validation (schema: $defs/Tender) ──
            if (retailEvent.Transaction.Tenders != null)
            {
                for (int i = 0; i < retailEvent.Transaction.Tenders.Count; i++)
                {
                    var tender = retailEvent.Transaction.Tenders[i];
                    string prefix = $"transaction.tenders[{i}]";

                    // tenderId (required)
                    if (string.IsNullOrEmpty(tender.TenderId))
                        errors.Add($"Missing required field: {prefix}.tenderId");

                    // method (required)
                    if (string.IsNullOrEmpty(tender.Method))
                        errors.Add($"Missing required field: {prefix}.method");

                    // amount (required Money)
                    if (tender.Amount == null)
                        errors.Add($"Missing required field: {prefix}.amount");
                    else
                        ValidateMoney(tender.Amount, $"{prefix}.amount", errors);
                }
            }
        }

        return errors;
    }

    // Validates a Money object per ORAE schema: both currency (ISO-4217) and value are required
    private static void ValidateMoney(CurrencyAmount money, string path, List<string> errors)
    {
        if (string.IsNullOrEmpty(money.Currency))
            errors.Add($"Missing required field: {path}.currency");
        else if (!Regex.IsMatch(money.Currency, @"^[A-Z]{3}$"))
            errors.Add($"Invalid {path}.currency: '{money.Currency}'. Must be ISO-4217 (3 uppercase letters)");

        if (string.IsNullOrEmpty(money.Value))
            errors.Add($"Missing required field: {path}.value");
        else if (!Regex.IsMatch(money.Value, @"^-?[0-9]{1,18}(\.[0-9]{1,4})?$"))
            errors.Add($"Invalid {path}.value: '{money.Value}'. Must be signed decimal string (e.g., '12.34', '-5.00')");
    }

    private static bool IsValidEnum(string value, string[] validValues)
    {
        return validValues.Contains(value);
    }
}
