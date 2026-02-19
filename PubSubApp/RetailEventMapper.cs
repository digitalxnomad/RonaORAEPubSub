using System.Text.Json;
using PubSubApp.Models;

namespace PubSubApp;

/// <summary>
/// Maps ORAE RetailEvent input to RIMSLF/RIMTNF RecordSet output.
/// </summary>
class RetailEventMapper
{
    public RecordSet MapRetailEventToRecordSet(RetailEvent retailEvent)
    {
        // Check for employee discount (will set SLFPVC and affect SLFTTP/SLFLNT)
        bool hasEmployeeDiscount = HasEmployeeDiscount(retailEvent);

        // Check for gift card tender
        bool hasGiftCardTender = HasGiftCardTender(retailEvent);

        // Check for customer ID (currently always blank, but ready for future)
        bool hasCustomerId = !string.IsNullOrEmpty(GetCustomerId(retailEvent));

        string mappedTransactionTypeSLFTTP = retailEvent.Transaction?.TransactionType != null ?
            MapTransTypeSLFTTP(retailEvent.Transaction.TransactionType, hasEmployeeDiscount) : "01";
        string mappedTransactionTypeSLFLNT = retailEvent.Transaction?.TransactionType != null ?
            MapTransTypeSLFLNT(retailEvent.Transaction.TransactionType, hasEmployeeDiscount, hasGiftCardTender, hasCustomerId) : "01";

        // Parse storeId as integer for PolledStore fields
        int? polledStoreInt = null;
        if (int.TryParse(retailEvent.BusinessContext?.Store?.StoreId, out int storeId))
        {
            polledStoreInt = storeId;
        }

        // Apply timezone adjustment using ORAE timeZone (IANA), falling back to store region heuristic
        DateTime transactionDateTime = ApplyTimezoneAdjustment(
            retailEvent.OccurredAt,
            retailEvent.BusinessContext?.Store?.TimeZone,
            retailEvent.BusinessContext?.Store?.StoreId);

        // Get date/time values from adjusted transaction time
        int pollCen = 1;  // Always 1 per specification
        int pollDate = GetDateAsInt(transactionDateTime); // SLFPDT - Use adjusted transaction date
        int createCen = 1;  // Always 1 per specification
        int createDate = pollDate;
        int createTime = GetTimeAsInt(transactionDateTime);

        // Calculate SLFSPS - SalesPerson ID: If register starts with 8 (SCO), use register ID; otherwise ACO uses "00000"
        string? salesPersonId;
        string registerId = retailEvent.BusinessContext?.Workstation?.RegisterId ?? "";
        if (registerId.StartsWith("8"))
        {
            // SCO (Self-Checkout) - use Workstation.RegisterID padded with zeros to 5 digits
            salesPersonId = PadNumeric(registerId, 5);
        }
        else
        {
            // ACO (Assisted Checkout) - use actor.cashier.loginId padded with zeros to 5 digits
            string cashierLoginId = retailEvent.Actor?.Cashier?.LoginId ?? "";
            salesPersonId = PadNumeric(cashierLoginId, 5);
        }

        var recordSet = new RecordSet
        {
            OrderRecords = new List<OrderRecord>(),
            TenderRecords = new List<TenderRecord>()
        };

        // Temporary lists to group records by type
        List<OrderRecord> itemRecords = new List<OrderRecord>();
        List<OrderRecord> taxRecords = new List<OrderRecord>();
        // Track lineId to index mapping for EPP parent lookup
        Dictionary<string, int> lineIdToIndex = new Dictionary<string, int>();
        // Store computed TaxRateCode per item (for use by tax records)
        string lastComputedTaxRateCode = "";

        // Map ALL items (not just first one) - create one OrderRecord per item
        if (retailEvent.Transaction?.Items != null && retailEvent.Transaction.Items.Count > 0)
        {
            foreach (var item in retailEvent.Transaction.Items)
            {
                var orderRecord = new OrderRecord
                {
                    // Required Fields - per CSV specs
                    // EPP items (x-epp-coverage-identifier = "9") get SLFTTP = "21" and SLFLNT = "21"
                    TransType = GetEPPCoverageIdentifier(item) == "9" ? "21" : mappedTransactionTypeSLFTTP,
                    LineType = GetEPPCoverageIdentifier(item) == "9" ? "21" : mappedTransactionTypeSLFLNT,
                    TransDate = transactionDateTime.ToString("yyMMdd"), // TNFTDT - Use timezone-adjusted date
                    TransTime = transactionDateTime.ToString("HHmmss"),
                    // Transaction Identification - with proper padding
                    TransNumber = PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                    TransSeq = "00000", // Placeholder - will be updated after grouping
                    RegisterID = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),

                    // Store Information
                    PolledStore = polledStoreInt,
                    PollCen = pollCen,
                    PollDate = pollDate,
                    CreateCen = createCen,
                    CreateDate = createDate,
                    CreateTime = createTime,

                    // Status - default to active (space for active)
                    Status = " "
                };

                // Track source lineId and parentLineId for console output
                orderRecord.SourceLineId = item.LineId;
                orderRecord.SourceParentLineId = item.ParentLineId;

                // SKU Number - 9-digits with leading zeros
                orderRecord.SKUNumber = PadNumeric(item.Item?.Sku, 9);

                // Quantity - 9-digits without decimal (multiply by 100)
                if (item.Quantity != null)
                {
                    decimal qtyValue = item.Quantity.Value;
                    bool isNegative = qtyValue < 0;
                    int qtyCents = (int)(Math.Abs(qtyValue) * 100);

                    orderRecord.Quantity = qtyCents.ToString().PadLeft(9, '0');
                    // SLFQTN: "-" for Return, "" for all else
                    orderRecord.QuantityNegativeSign = retailEvent.Transaction?.TransactionType == "RETURN" ? "-" : "";
                }

                // Original Price - 9-digits without decimal
                if (item.Pricing?.OriginalUnitPrice?.Value != null)
                {
                    var (amount, sign) = FormatCurrencyWithSign(item.Pricing.OriginalUnitPrice.Value, 9);
                    orderRecord.OriginalPrice = amount;
                    orderRecord.OriginalPriceNegativeSign = sign;
                    orderRecord.OriginalRetail = amount; // SLFORT same as SLFORG
                    orderRecord.OriginalRetailNegativeSign = sign;
                }

                // SLFSEL - Item Sell Price - 9-digits without decimal
                // Use override price if it exists and is non-zero; otherwise use UnitPrice
                // Check both pricing.override and pricing.priceOverride.overrideUnitPrice
                decimal overridePrice = 0;
                bool hasOverride = false;
                string? overridePriceValue = null;

                // First check priceOverride.overrideUnitPrice (new structure)
                if (item.Pricing?.PriceOverride?.OverrideUnitPrice?.Value != null &&
                    decimal.TryParse(item.Pricing.PriceOverride.OverrideUnitPrice.Value, out overridePrice) &&
                    overridePrice != 0)
                {
                    hasOverride = true;
                    overridePriceValue = item.Pricing.PriceOverride.OverrideUnitPrice.Value;
                }
                // Fallback to pricing.override (legacy structure)
                else if (item.Pricing?.Override?.Value != null &&
                    decimal.TryParse(item.Pricing.Override.Value, out overridePrice) &&
                    overridePrice != 0)
                {
                    hasOverride = true;
                    overridePriceValue = item.Pricing.Override.Value;
                }

                string? sellPriceSource = hasOverride
                    ? overridePriceValue
                    : item.Pricing?.UnitPrice?.Value;

                if (sellPriceSource != null)
                {
                    var (amount, sign) = FormatCurrencyWithSign(sellPriceSource, 9);
                    orderRecord.ItemSellPrice = amount;
                    orderRecord.SellPriceNegativeSign = sign;
                }

                // SLFEXT - Extended Value - 11-digits without decimal
                // Quantity * Override (if exists and non-zero), otherwise Quantity * UnitPrice
                decimal extendedValue = 0;
                if (item.Quantity?.Value != null)
                {
                    decimal quantity = item.Quantity.Value;

                    if (hasOverride)
                    {
                        extendedValue = quantity * overridePrice;
                    }
                    else if (item.Pricing?.UnitPrice?.Value != null &&
                             decimal.TryParse(item.Pricing.UnitPrice.Value, out decimal unitPriceVal))
                    {
                        extendedValue = quantity * unitPriceVal;
                    }
                }

                var (amountExt, signExt) = FormatCurrencyWithSign(extendedValue.ToString("F2"), 11);
                orderRecord.ExtendedValue = amountExt;
                orderRecord.ExtendedValueNegativeSign = signExt;

                // Override Price - 9-digit format without decimal, default to zeros if not present
                var (overrideAmt, overrideSign) = FormatCurrencyWithSign(overridePrice.ToString("F2"), 9);
                orderRecord.OverridePrice = overrideAmt;
                orderRecord.OverridePriceNegativeSign = overrideSign;

                // SLFDSA - Discount Amount should always be 9 zeros for order records
                // SLFDST - Discount Type should always be blank for order records
                orderRecord.DiscountAmount = "000000000";
                orderRecord.DiscountType = "";
                orderRecord.DiscountAmountNegativeSign = "";

                // Item Scanned Y/N
                // SLFSCN - "Y" if item.gtin > 0, otherwise "N"
                bool hasGtin = !string.IsNullOrEmpty(item.Item?.Gtin) &&
                    decimal.TryParse(item.Item.Gtin, out decimal gtinVal) && gtinVal > 0;
                orderRecord.ItemScanned = hasGtin ? "Y" : "N";

                // Price Vehicle Code (SLFPVC) and Reference (SLFREF) - parse from pricing.priceVehicle
                // Format: "LEFT:RIGHT" where LEFT goes to SLFPVC (4 chars) and RIGHT goes to SLFREF (12 chars)
                if (!string.IsNullOrEmpty(item.Pricing?.PriceVehicle))
                {
                    string[] parts = item.Pricing.PriceVehicle.Split(':');
                    if (parts.Length == 2)
                    {
                        orderRecord.PriceVehicleCode = PadOrTruncate(parts[0], 4); // Left side to SLFPVC
                        orderRecord.PriceVehicleReference = PadOrTruncate(parts[1], 12); // Right side to SLFREF
                    }
                    else
                    {
                        // If format is incorrect, pad the whole value to SLFPVC
                        orderRecord.PriceVehicleCode = PadOrTruncate(item.Pricing.PriceVehicle, 4);
                        orderRecord.PriceVehicleReference = PadOrTruncate("", 12);
                    }
                }
                else
                {
                    // Fallback: use "EMP" for employee discounts if priceVehicle not provided
                    orderRecord.PriceVehicleCode = hasEmployeeDiscount ? PadOrTruncate("EMP", 4) : PadOrTruncate("", 4);
                    orderRecord.PriceVehicleReference = PadOrTruncate("", 12);
                }

                // SLFADC - Additional Code: "####" when POS price != regular retail, "0000" when regular price
                bool isRegularPrice = true;
                if (item.Pricing?.OriginalUnitPrice?.Value != null &&
                    item.Pricing?.UnitPrice?.Value != null &&
                    decimal.TryParse(item.Pricing.OriginalUnitPrice.Value, out decimal origPriceAdc) &&
                    decimal.TryParse(item.Pricing.UnitPrice.Value, out decimal unitPriceAdc))
                {
                    isRegularPrice = (unitPriceAdc == origPriceAdc);
                }
                orderRecord.AdCode = isRegularPrice ? "0000" : "####";

                // SLFADP - Ad Price: "000000000" when PVCode is REG or MAN, otherwise unit price
                string pvCode = orderRecord.PriceVehicleCode?.Trim() ?? "";
                if (pvCode == "REG" || pvCode == "MAN")
                {
                    orderRecord.AdPrice = "000000000"; // 9 zeros for REG or MAN
                    orderRecord.AdPriceNegativeSign = ""; // Empty sign
                }
                else
                {
                    // Use unit price formatted as 9-digit currency
                    if (item.Pricing?.UnitPrice?.Value != null)
                    {
                        var (amount, sign) = FormatCurrencyWithSign(item.Pricing.UnitPrice.Value, 9);
                        orderRecord.AdPrice = amount;
                        orderRecord.AdPriceNegativeSign = sign;
                    }
                    else
                    {
                        orderRecord.AdPrice = "000000000";
                        orderRecord.AdPriceNegativeSign = "";
                    }
                }

                // Parse item-level taxes (returns computed TaxRateCode for tax records)
                lastComputedTaxRateCode = ParseItemTaxes(item, orderRecord, retailEvent);

                // Reference fields
                orderRecord.ReferenceCode = ""; // SLFRFC - Always empty string
                orderRecord.ReferenceDesc = ""; // SLFRFD - Always empty string

                // SLFOTS, SLFOTD, SLFOTR, SLFOTT - Set based on transaction type
                if (mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43") // SALE, Employee SALE, AR Payment
                {
                    orderRecord.OriginalTxStore = "00000"; // 5 zeros for sales
                    orderRecord.OriginalTxDate = "000000"; // SLFOTD - 6 zeros for sales
                    orderRecord.OriginalTxRegister = "000"; // SLFOTR - 3 zeros for sales
                    orderRecord.OriginalTxNumber = "00000"; // SLFOTT - 5 zeros for sales
                }
                else if (mappedTransactionTypeSLFTTP == "11") // RETURN
                {
                    orderRecord.OriginalTxStore = PadOrTruncate(retailEvent.BusinessContext?.Store?.StoreId, 5);
                    orderRecord.OriginalTxDate = transactionDateTime.ToString("yyMMdd");
                    orderRecord.OriginalTxRegister = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3);
                    // Use sourceTransactionId if available for returns
                    orderRecord.OriginalTxNumber = retailEvent.References?.SourceTransactionId != null
                        ? PadOrTruncate(retailEvent.References.SourceTransactionId, 5)
                        : PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5);
                }
                else if (mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88") // Current VOID or Post VOID
                {
                    orderRecord.OriginalTxStore = PadOrTruncate(retailEvent.BusinessContext?.Store?.StoreId, 5);
                    orderRecord.OriginalTxDate = transactionDateTime.ToString("yyMMdd");
                    orderRecord.OriginalTxRegister = PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3);
                    orderRecord.OriginalTxNumber = PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5);
                }

                // === CSV-specified field mappings ===

                // Customer fields - per CSV rules
                orderRecord.CustomerName = ""; // SLFCNM - Always blank
                // SLFNUM - 10-digit customer number with leading zeros, blank if no customer
                string customerId = GetCustomerId(retailEvent);
                orderRecord.CustomerNumber = !string.IsNullOrEmpty(customerId)
                    ? customerId.PadLeft(10, '0')
                    : "";

                // SLFZIP - 9 spaces + EPP eligibility digit (10 chars total)
                // 9 = This line item IS the EPP (has x-epp-coverage-identifier attribute)
                // 0 = Not an EPP item
                string eppDigit = GetEPPCoverageIdentifier(item) != null ? "9" : "0";
                orderRecord.ZipCode = "         " + eppDigit; // 9 spaces + digit

                // Till/Clerk - SLFCLK (from till number) - right justified with zeros
                orderRecord.Clerk = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 5);

                // Employee fields - per CSV rules
                orderRecord.EmployeeCardNumber = 0; // SLFECN - Set to zero

                // UPC Code - SLFUPC - from item.gtin, rightmost 13 chars (chop left if longer), pad left with zeros if shorter
                orderRecord.UPCCode = !string.IsNullOrEmpty(item.Item?.Gtin)
                    ? item.Item.Gtin.Length > 13
                        ? item.Item.Gtin.Substring(item.Item.Gtin.Length - 13)
                        : item.Item.Gtin.PadLeft(13, '0')
                    : "0000000000000";

                // Email - SLFEML (for ereceipt)
                orderRecord.EReceiptEmail = ""; // Default blank, TODO: populate if ereceipt scenario

                // Reason codes - SLFRSN (16 chars, left justified)
                // RRT0 = Return, POV0 = Price Override (OVD:OVR), IDS0 = Manual Discount, VOD0 = Post Voided
                string reasonCode = "                "; // 16 blank spaces default
                string transType = retailEvent.Transaction?.TransactionType ?? "";
                string priceVehicle = item.Pricing?.PriceVehicle ?? "";
                string pvCodeForRsn = orderRecord.PriceVehicleCode?.Trim() ?? "";
                string overrideReason = item.Pricing?.PriceOverride?.Reason ?? "";

                if (transType == "RETURN")
                    reasonCode = "RRT0";
                else if (transType == "VOID")
                    reasonCode = "VOD0";
                else if (priceVehicle == "OVD:OVR")
                    reasonCode = "POV0" + overrideReason; // POV0 + priceOverride.reason (e.g. "POV01504")
                else if (pvCodeForRsn == "MAN")
                    reasonCode = "IDS0";
                orderRecord.ReasonCode = reasonCode.PadRight(16);

                // Tax exemption fields - SLFTE1, SLFTE2, SLFTEN - always empty
                orderRecord.TaxExemptId1 = ""; // SLFTE1 - Always empty
                orderRecord.TaxExemptId2 = ""; // SLFTE2 - Always empty
                orderRecord.TaxExemptionName = ""; // SLFTEN - Always empty

                // Required fields with fixed values - per validation spec
                orderRecord.OriginalSalesperson = "00000"; // SLFOSP - Required, must be "00000"
                orderRecord.OriginalStore = "00000"; // SLFOST - Required, must be "00000"
                orderRecord.GroupDiscAmount = "000000000"; // SLFGDA - Required, must be "000000000"
                orderRecord.GroupDiscSign = ""; // SLFGDS - Must be empty string
                orderRecord.SalesPerson = salesPersonId; // SLFSPS - SCO uses register ID, ACO uses "00000"

                // Discount reasons - per CSV rules
                orderRecord.GroupDiscReason = "00"; // SLFGDR - Always '00'

                // SLFRDR - Set to 'I2' when PVC = 'MAN', otherwise '00'
                string pvCodeForRdr = orderRecord.PriceVehicleCode?.Trim() ?? "";
                orderRecord.RegDiscReason = (pvCodeForRdr == "MAN") ? "I2" : "00";

                // Blank fields - per CSV rules
                orderRecord.OrderNumber = ""; // SLFORD - Always blank
                orderRecord.ProjectNumber = ""; // ASFPRO - Always blank
                orderRecord.SalesStore = 0; // ASFSST - Always blank (0)
                orderRecord.InvStore = 0; // ASFIST - Always blank (0)

                // Add this OrderRecord to the item records list
                itemRecords.Add(orderRecord);

                // Track lineId to record index for EPP parent lookup
                if (!string.IsNullOrEmpty(item.LineId))
                {
                    lineIdToIndex[item.LineId] = itemRecords.Count - 1;
                }
            }

            // Reorder: EPP items (identifier="9") go right after their parent SKU using parentLineId
            // Build ordered list: for each non-EPP-9 item, insert any EPP-9 items that reference it
            List<OrderRecord> reorderedRecords = new List<OrderRecord>();
            HashSet<int> placed = new HashSet<int>();

            // Build a map of parentLineId -> list of EPP record indices
            Dictionary<string, List<int>> eppChildMap = new Dictionary<string, List<int>>();
            if (retailEvent.Transaction?.Items != null)
            {
                int recordIdx = 0;
                foreach (var txItem in retailEvent.Transaction.Items)
                {
                    if (recordIdx < itemRecords.Count)
                    {
                        string? covId = GetEPPCoverageIdentifier(txItem);
                        // EPP items (identifier="9") with a parentLineId should follow their parent
                        if (covId == "9" && !string.IsNullOrEmpty(txItem.ParentLineId))
                        {
                            if (!eppChildMap.ContainsKey(txItem.ParentLineId))
                                eppChildMap[txItem.ParentLineId] = new List<int>();
                            eppChildMap[txItem.ParentLineId].Add(recordIdx);
                        }
                    }
                    recordIdx++;
                }
            }

            // Now build ordered list
            if (retailEvent.Transaction?.Items != null)
            {
                int recordIdx = 0;
                foreach (var txItem in retailEvent.Transaction.Items)
                {
                    if (recordIdx >= itemRecords.Count) break;

                    string? covId = GetEPPCoverageIdentifier(txItem);

                    // Skip EPP-9 items here - they'll be inserted after their parent via eppChildMap
                    if (covId == "9" && !string.IsNullOrEmpty(txItem.ParentLineId))
                    {
                        recordIdx++;
                        continue;
                    }

                    // Add the item record
                    if (!placed.Contains(recordIdx))
                    {
                        reorderedRecords.Add(itemRecords[recordIdx]);
                        placed.Add(recordIdx);
                    }
                    recordIdx++;

                    // Insert any EPP-9 children that reference this item's lineId
                    if (!string.IsNullOrEmpty(txItem.LineId) && eppChildMap.ContainsKey(txItem.LineId))
                    {
                        foreach (int eppIdx in eppChildMap[txItem.LineId])
                        {
                            if (!placed.Contains(eppIdx) && eppIdx < itemRecords.Count)
                            {
                                reorderedRecords.Add(itemRecords[eppIdx]);
                                placed.Add(eppIdx);
                            }
                        }
                    }
                }
            }

            // Add any remaining records that weren't placed
            for (int i = 0; i < itemRecords.Count; i++)
            {
                if (!placed.Contains(i))
                    reorderedRecords.Add(itemRecords[i]);
            }
            itemRecords = reorderedRecords;

            // Determine if this is an Ontario transaction
            string? province = GetProvince(retailEvent);
            bool isOntario = province?.ToUpper() == "ON" || province?.ToUpper() == "ONTARIO";
            bool isGstPstProvince = province?.ToUpper() is "BC" or "MB" or "SK" or "QC";

            // For Ontario: Create up to TWO tax records:
            //   - HON (13% HST) - accumulated sum of all HST taxes
            //   - HON1 (5% GST/Partial HST) - accumulated sum of all non-HST taxes
            // For GST+PST/QST provinces (BC, MB, SK, QC): Create up to TWO consolidated tax records:
            //   - Federal GST (FED -> XG) - accumulated sum of all federal taxes across all items
            //   - Provincial PST/QST (BC/MB/SK/PQ -> XR/XM/XS/XQ) - accumulated sum of all provincial taxes
            // For other provinces: Create per-item tax records
            if (isOntario)
            {
                // Separate tax totals for HST (13%) and non-HST (5% GST)
                decimal hstTaxTotal = 0;    // HON - 13% HST
                decimal nonHstTaxTotal = 0; // HON1 - 5% GST/Partial HST
                string hstTaxRateCode = "";
                string nonHstTaxRateCode = "";

                foreach (var item in retailEvent.Transaction?.Items ?? new List<TransactionItem>())
                {
                    if (item.Taxes != null)
                    {
                        foreach (var tax in item.Taxes)
                        {
                            if (tax.TaxAmount?.Value != null && decimal.TryParse(tax.TaxAmount.Value, out decimal taxAmount))
                            {
                                // Determine if this is HST (13%) or non-HST (5%) based on rate or jurisdiction
                                bool isHst = false;

                                // Check by rate percent
                                if (!string.IsNullOrEmpty(tax.RatePercent) && decimal.TryParse(tax.RatePercent, out decimal ratePercent))
                                {
                                    isHst = ratePercent >= 10; // 13% is HST, 5% is GST
                                }
                                // Check by tax rate (decimal form)
                                else if (tax.TaxRate != null)
                                {
                                    isHst = tax.TaxRate.Value >= 0.10m; // 0.13 is HST, 0.05 is GST
                                }
                                // Check by jurisdiction region
                                else if (!string.IsNullOrEmpty(tax.Jurisdiction?.Region))
                                {
                                    string region = tax.Jurisdiction.Region.ToUpper();
                                    isHst = region == "HON" || region.Contains("HST");
                                }

                                if (isHst)
                                {
                                    hstTaxTotal += taxAmount;
                                    if (string.IsNullOrEmpty(hstTaxRateCode) && !string.IsNullOrEmpty(tax.TaxCode))
                                        hstTaxRateCode = tax.TaxCode;
                                }
                                else
                                {
                                    nonHstTaxTotal += taxAmount;
                                    if (string.IsNullOrEmpty(nonHstTaxRateCode) && !string.IsNullOrEmpty(tax.TaxCode))
                                        nonHstTaxRateCode = tax.TaxCode;
                                }
                            }
                        }
                    }
                }

                // Create HST tax record (HON - 13%) if there's HST tax
                if (hstTaxTotal != 0)
                {
                    var hstTaxRecord = new OrderRecord
                    {
                        TransType = mappedTransactionTypeSLFTTP,
                        LineType = "XH", // HON -> XH
                        TransDate = transactionDateTime.ToString("yyMMdd"),
                        TransTime = transactionDateTime.ToString("HHmmss"),
                        TransNumber = PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                        TransSeq = "00000",
                        RegisterID = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),
                        PolledStore = polledStoreInt,
                        PollCen = pollCen,
                        PollDate = pollDate,
                        CreateCen = createCen,
                        CreateDate = createDate,
                        CreateTime = createTime,
                        Status = " ",
                        ChargedTax1 = "N",
                        ChargedTax2 = "N",
                        ChargedTax3 = "N",
                        ChargedTax4 = "N",
                        TaxAuthCode = PadOrTruncate("HON", 6),
                        TaxRateCode = PadOrTruncate(!string.IsNullOrEmpty(hstTaxRateCode) ? hstTaxRateCode : lastComputedTaxRateCode, 6),
                        ExtendedValue = FormatCurrency(hstTaxTotal.ToString("F2"), 11),
                        ExtendedValueNegativeSign = hstTaxTotal < 0 ? "-" : "",
                        ItemSellPrice = FormatCurrency(hstTaxTotal.ToString("F2"), 9),
                        SellPriceNegativeSign = hstTaxTotal < 0 ? "-" : "",
                        SKUNumber = "000000000",
                        Quantity = "000000100",
                        QuantityNegativeSign = retailEvent.Transaction?.TransactionType == "RETURN" ? "-" : "",
                        OriginalPrice = "000000000",
                        OriginalPriceNegativeSign = "",
                        OverridePrice = "000000000",
                        OverridePriceNegativeSign = "",
                        OriginalRetail = "000000000",
                        OriginalRetailNegativeSign = "",
                        ReferenceCode = "",
                        ReferenceDesc = "",
                        OriginalTxStore = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "00000" :
                                         (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadOrTruncate(retailEvent.BusinessContext?.Store?.StoreId, 5) : null),
                        OriginalTxDate = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "000000" :
                                        (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? transactionDateTime.ToString("yyMMdd") : null),
                        OriginalTxRegister = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "000" :
                                            (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3) : null),
                        OriginalTxNumber = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "00000" :
                                          (mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5) :
                                          (mappedTransactionTypeSLFTTP == "11" && retailEvent.References?.SourceTransactionId != null ? PadOrTruncate(retailEvent.References.SourceTransactionId, 5) :
                                          PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5))),
                        CustomerName = "",
                        CustomerNumber = "",
                        ZipCode = "         0",
                        Clerk = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 5),
                        EmployeeCardNumber = 0,
                        UPCCode = "0000000000000",
                        EReceiptEmail = "",
                        ReasonCode = "",
                        TaxExemptId1 = "",
                        TaxExemptId2 = "",
                        TaxExemptionName = "",
                        AdCode = "0000",
                        AdPrice = "000000000",
                        AdPriceNegativeSign = "",
                        PriceVehicleCode = "",
                        PriceVehicleReference = "",
                        OriginalSalesperson = "00000",
                        OriginalStore = "00000",
                        GroupDiscAmount = "000000000",
                        GroupDiscSign = "",
                        SalesPerson = salesPersonId,
                        DiscountAmount = "000000000",
                        DiscountType = "",
                        DiscountAmountNegativeSign = "",
                        GroupDiscReason = "00",
                        RegDiscReason = "00",
                        OrderNumber = "",
                        ProjectNumber = "",
                        SalesStore = 0,
                        InvStore = 0,
                        ItemScanned = ""
                    };
                    taxRecords.Add(hstTaxRecord);
                }

                // Create non-HST tax record (HON1 - 5% GST) if there's non-HST tax
                if (nonHstTaxTotal != 0)
                {
                    var nonHstTaxRecord = new OrderRecord
                    {
                        TransType = mappedTransactionTypeSLFTTP,
                        LineType = "XI", // HON1 -> XI
                        TransDate = transactionDateTime.ToString("yyMMdd"),
                        TransTime = transactionDateTime.ToString("HHmmss"),
                        TransNumber = PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                        TransSeq = "00000",
                        RegisterID = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),
                        PolledStore = polledStoreInt,
                        PollCen = pollCen,
                        PollDate = pollDate,
                        CreateCen = createCen,
                        CreateDate = createDate,
                        CreateTime = createTime,
                        Status = " ",
                        ChargedTax1 = "N",
                        ChargedTax2 = "N",
                        ChargedTax3 = "N",
                        ChargedTax4 = "N",
                        TaxAuthCode = PadOrTruncate("HON1", 6),
                        TaxRateCode = PadOrTruncate(!string.IsNullOrEmpty(nonHstTaxRateCode) ? nonHstTaxRateCode : lastComputedTaxRateCode, 6),
                        ExtendedValue = FormatCurrency(nonHstTaxTotal.ToString("F2"), 11),
                        ExtendedValueNegativeSign = nonHstTaxTotal < 0 ? "-" : "",
                        ItemSellPrice = FormatCurrency(nonHstTaxTotal.ToString("F2"), 9),
                        SellPriceNegativeSign = nonHstTaxTotal < 0 ? "-" : "",
                        SKUNumber = "000000000",
                        Quantity = "000000100",
                        QuantityNegativeSign = retailEvent.Transaction?.TransactionType == "RETURN" ? "-" : "",
                        OriginalPrice = "000000000",
                        OriginalPriceNegativeSign = "",
                        OverridePrice = "000000000",
                        OverridePriceNegativeSign = "",
                        OriginalRetail = "000000000",
                        OriginalRetailNegativeSign = "",
                        ReferenceCode = "",
                        ReferenceDesc = "",
                        OriginalTxStore = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "00000" :
                                         (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadOrTruncate(retailEvent.BusinessContext?.Store?.StoreId, 5) : null),
                        OriginalTxDate = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "000000" :
                                        (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? transactionDateTime.ToString("yyMMdd") : null),
                        OriginalTxRegister = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "000" :
                                            (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3) : null),
                        OriginalTxNumber = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "00000" :
                                          (mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5) :
                                          (mappedTransactionTypeSLFTTP == "11" && retailEvent.References?.SourceTransactionId != null ? PadOrTruncate(retailEvent.References.SourceTransactionId, 5) :
                                          PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5))),
                        CustomerName = "",
                        CustomerNumber = "",
                        ZipCode = "         0",
                        Clerk = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 5),
                        EmployeeCardNumber = 0,
                        UPCCode = "0000000000000",
                        EReceiptEmail = "",
                        ReasonCode = "",
                        TaxExemptId1 = "",
                        TaxExemptId2 = "",
                        TaxExemptionName = "",
                        AdCode = "0000",
                        AdPrice = "000000000",
                        AdPriceNegativeSign = "",
                        PriceVehicleCode = "",
                        PriceVehicleReference = "",
                        OriginalSalesperson = "00000",
                        OriginalStore = "00000",
                        GroupDiscAmount = "000000000",
                        GroupDiscSign = "",
                        SalesPerson = salesPersonId,
                        DiscountAmount = "000000000",
                        DiscountType = "",
                        DiscountAmountNegativeSign = "",
                        GroupDiscReason = "00",
                        RegDiscReason = "00",
                        OrderNumber = "",
                        ProjectNumber = "",
                        SalesStore = 0,
                        InvStore = 0,
                        ItemScanned = ""
                    };
                    taxRecords.Add(nonHstTaxRecord);
                }
            }
            else if (isGstPstProvince)
            {
                // GST+PST/QST provinces (BC, MB, SK, QC): Create up to TWO consolidated tax records
                // 1. Federal GST (FED -> XG) - accumulated across all items
                // 2. Provincial PST/QST (province-specific auth code) - accumulated across all items
                decimal federalTaxTotal = 0;
                decimal provincialTaxTotal = 0;
                string federalTaxRateCode = "";
                string provincialTaxRateCode = "";

                foreach (var item in retailEvent.Transaction?.Items ?? new List<TransactionItem>())
                {
                    if (item.Taxes != null)
                    {
                        foreach (var tax in item.Taxes)
                        {
                            if (tax.TaxAmount?.Value != null && decimal.TryParse(tax.TaxAmount.Value, out decimal taxAmount))
                            {
                                string? taxType = tax.TaxType?.ToUpper() ?? tax.TaxCategory?.ToUpper();

                                bool isFederal = taxType is "GST" or "FEDERAL" or "VAT" or "NATIONAL";
                                bool isProvincial = taxType is "PST" or "QST" or "PROVINCIAL" or "STATE" or "QUEBEC";

                                // Fallback: if taxType is not set, try to determine from rate
                                if (!isFederal && !isProvincial)
                                {
                                    if (!string.IsNullOrEmpty(tax.RatePercent) && decimal.TryParse(tax.RatePercent, out decimal ratePercent))
                                    {
                                        isFederal = ratePercent >= 4 && ratePercent <= 6; // ~5% GST
                                        isProvincial = !isFederal;
                                    }
                                    else if (tax.TaxRate != null)
                                    {
                                        isFederal = tax.TaxRate.Value >= 0.04m && tax.TaxRate.Value <= 0.06m; // ~5% GST
                                        isProvincial = !isFederal;
                                    }
                                    else
                                    {
                                        // Default: treat unknown as federal GST
                                        isFederal = true;
                                    }
                                }

                                if (isFederal)
                                {
                                    federalTaxTotal += taxAmount;
                                    if (string.IsNullOrEmpty(federalTaxRateCode) && !string.IsNullOrEmpty(tax.TaxCode))
                                        federalTaxRateCode = tax.TaxCode;
                                }
                                else if (isProvincial)
                                {
                                    provincialTaxTotal += taxAmount;
                                    if (string.IsNullOrEmpty(provincialTaxRateCode) && !string.IsNullOrEmpty(tax.TaxCode))
                                        provincialTaxRateCode = tax.TaxCode;
                                }
                            }
                        }
                    }
                }

                // Map province to provincial tax authority code
                string provincialTaxAuth = province!.ToUpper() switch
                {
                    "QC" => "PQ",
                    _ => province.ToUpper() // BC, MB, SK map directly
                };

                // Create Federal GST tax record if there's federal tax
                if (federalTaxTotal != 0)
                {
                    var federalTaxRecord = new OrderRecord
                    {
                        TransType = mappedTransactionTypeSLFTTP,
                        LineType = MapTaxAuthToLineType("FED"), // XG
                        TransDate = transactionDateTime.ToString("yyMMdd"),
                        TransTime = transactionDateTime.ToString("HHmmss"),
                        TransNumber = PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                        TransSeq = "00000",
                        RegisterID = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),
                        PolledStore = polledStoreInt,
                        PollCen = pollCen,
                        PollDate = pollDate,
                        CreateCen = createCen,
                        CreateDate = createDate,
                        CreateTime = createTime,
                        Status = " ",
                        ChargedTax1 = "N",
                        ChargedTax2 = "N",
                        ChargedTax3 = "N",
                        ChargedTax4 = "N",
                        TaxAuthCode = PadOrTruncate("FED", 6),
                        TaxRateCode = PadOrTruncate(!string.IsNullOrEmpty(federalTaxRateCode) ? federalTaxRateCode : lastComputedTaxRateCode, 6),
                        ExtendedValue = FormatCurrency(federalTaxTotal.ToString("F2"), 11),
                        ExtendedValueNegativeSign = federalTaxTotal < 0 ? "-" : "",
                        ItemSellPrice = FormatCurrency(federalTaxTotal.ToString("F2"), 9),
                        SellPriceNegativeSign = federalTaxTotal < 0 ? "-" : "",
                        SKUNumber = "000000000",
                        Quantity = "000000100",
                        QuantityNegativeSign = retailEvent.Transaction?.TransactionType == "RETURN" ? "-" : "",
                        OriginalPrice = "000000000",
                        OriginalPriceNegativeSign = "",
                        OverridePrice = "000000000",
                        OverridePriceNegativeSign = "",
                        OriginalRetail = "000000000",
                        OriginalRetailNegativeSign = "",
                        ReferenceCode = "",
                        ReferenceDesc = "",
                        OriginalTxStore = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "00000" :
                                         (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadOrTruncate(retailEvent.BusinessContext?.Store?.StoreId, 5) : null),
                        OriginalTxDate = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "000000" :
                                        (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? transactionDateTime.ToString("yyMMdd") : null),
                        OriginalTxRegister = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "000" :
                                            (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3) : null),
                        OriginalTxNumber = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "00000" :
                                          (mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5) :
                                          (mappedTransactionTypeSLFTTP == "11" && retailEvent.References?.SourceTransactionId != null ? PadOrTruncate(retailEvent.References.SourceTransactionId, 5) :
                                          PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5))),
                        CustomerName = "",
                        CustomerNumber = "",
                        ZipCode = "         0",
                        Clerk = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 5),
                        EmployeeCardNumber = 0,
                        UPCCode = "0000000000000",
                        EReceiptEmail = "",
                        ReasonCode = "",
                        TaxExemptId1 = "",
                        TaxExemptId2 = "",
                        TaxExemptionName = "",
                        AdCode = "0000",
                        AdPrice = "000000000",
                        AdPriceNegativeSign = "",
                        PriceVehicleCode = "",
                        PriceVehicleReference = "",
                        OriginalSalesperson = "00000",
                        OriginalStore = "00000",
                        GroupDiscAmount = "000000000",
                        GroupDiscSign = "",
                        SalesPerson = salesPersonId,
                        DiscountAmount = "000000000",
                        DiscountType = "",
                        DiscountAmountNegativeSign = "",
                        GroupDiscReason = "00",
                        RegDiscReason = "00",
                        OrderNumber = "",
                        ProjectNumber = "",
                        SalesStore = 0,
                        InvStore = 0,
                        ItemScanned = ""
                    };
                    taxRecords.Add(federalTaxRecord);
                }

                // Create Provincial PST/QST tax record if there's provincial tax
                if (provincialTaxTotal != 0)
                {
                    var provincialTaxRecord = new OrderRecord
                    {
                        TransType = mappedTransactionTypeSLFTTP,
                        LineType = MapTaxAuthToLineType(provincialTaxAuth), // XR/XM/XS/XQ
                        TransDate = transactionDateTime.ToString("yyMMdd"),
                        TransTime = transactionDateTime.ToString("HHmmss"),
                        TransNumber = PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                        TransSeq = "00000",
                        RegisterID = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),
                        PolledStore = polledStoreInt,
                        PollCen = pollCen,
                        PollDate = pollDate,
                        CreateCen = createCen,
                        CreateDate = createDate,
                        CreateTime = createTime,
                        Status = " ",
                        ChargedTax1 = "N",
                        ChargedTax2 = "N",
                        ChargedTax3 = "N",
                        ChargedTax4 = "N",
                        TaxAuthCode = PadOrTruncate(provincialTaxAuth, 6),
                        TaxRateCode = PadOrTruncate(!string.IsNullOrEmpty(provincialTaxRateCode) ? provincialTaxRateCode : lastComputedTaxRateCode, 6),
                        ExtendedValue = FormatCurrency(provincialTaxTotal.ToString("F2"), 11),
                        ExtendedValueNegativeSign = provincialTaxTotal < 0 ? "-" : "",
                        ItemSellPrice = FormatCurrency(provincialTaxTotal.ToString("F2"), 9),
                        SellPriceNegativeSign = provincialTaxTotal < 0 ? "-" : "",
                        SKUNumber = "000000000",
                        Quantity = "000000100",
                        QuantityNegativeSign = retailEvent.Transaction?.TransactionType == "RETURN" ? "-" : "",
                        OriginalPrice = "000000000",
                        OriginalPriceNegativeSign = "",
                        OverridePrice = "000000000",
                        OverridePriceNegativeSign = "",
                        OriginalRetail = "000000000",
                        OriginalRetailNegativeSign = "",
                        ReferenceCode = "",
                        ReferenceDesc = "",
                        OriginalTxStore = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "00000" :
                                         (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadOrTruncate(retailEvent.BusinessContext?.Store?.StoreId, 5) : null),
                        OriginalTxDate = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "000000" :
                                        (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? transactionDateTime.ToString("yyMMdd") : null),
                        OriginalTxRegister = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "000" :
                                            (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3) : null),
                        OriginalTxNumber = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "00000" :
                                          (mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5) :
                                          (mappedTransactionTypeSLFTTP == "11" && retailEvent.References?.SourceTransactionId != null ? PadOrTruncate(retailEvent.References.SourceTransactionId, 5) :
                                          PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5))),
                        CustomerName = "",
                        CustomerNumber = "",
                        ZipCode = "         0",
                        Clerk = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 5),
                        EmployeeCardNumber = 0,
                        UPCCode = "0000000000000",
                        EReceiptEmail = "",
                        ReasonCode = "",
                        TaxExemptId1 = "",
                        TaxExemptId2 = "",
                        TaxExemptionName = "",
                        AdCode = "0000",
                        AdPrice = "000000000",
                        AdPriceNegativeSign = "",
                        PriceVehicleCode = "",
                        PriceVehicleReference = "",
                        OriginalSalesperson = "00000",
                        OriginalStore = "00000",
                        GroupDiscAmount = "000000000",
                        GroupDiscSign = "",
                        SalesPerson = salesPersonId,
                        DiscountAmount = "000000000",
                        DiscountType = "",
                        DiscountAmountNegativeSign = "",
                        GroupDiscReason = "00",
                        RegDiscReason = "00",
                        OrderNumber = "",
                        ProjectNumber = "",
                        SalesStore = 0,
                        InvStore = 0,
                        ItemScanned = ""
                    };
                    taxRecords.Add(provincialTaxRecord);
                }
            }
            else
            {
                // Other provinces: Create per-item tax records (original logic)
                foreach (var item in retailEvent.Transaction?.Items ?? new List<TransactionItem>())
                {
                    if (item.Taxes != null)
                    {
                        // Calculate total tax for this item
                        decimal itemTaxTotal = 0;
                        foreach (var tax in item.Taxes)
                        {
                            if (tax.TaxAmount?.Value != null && decimal.TryParse(tax.TaxAmount.Value, out decimal taxAmount))
                            {
                                itemTaxTotal += taxAmount;
                            }
                        }

                        // Only create tax line if there's a non-zero tax amount
                        if (itemTaxTotal != 0)
                        {
                            string taxAuthority = DetermineTaxAuthority(retailEvent, itemTaxTotal);
                            string taxLineType = MapTaxAuthToLineType(taxAuthority);

                            // SLFACD - from taxes.jurisdiction.region for tax records
                            string itemTaxAuthCode = "";
                            foreach (var tax in item.Taxes)
                            {
                                if (!string.IsNullOrEmpty(tax.Jurisdiction?.Region))
                                {
                                    itemTaxAuthCode = PadOrTruncate(tax.Jurisdiction.Region, 6) ?? "";
                                    break;
                                }
                            }

                            var taxRecord = new OrderRecord
                            {
                                // Same transaction identifiers as parent item
                                TransType = mappedTransactionTypeSLFTTP,
                                LineType = taxLineType, // SLFLNT from TBLFLD TAXTXC mapping
                                TransDate = transactionDateTime.ToString("yyMMdd"), // SLFTDT - Use timezone-adjusted date
                                TransTime = transactionDateTime.ToString("HHmmss"), // SLFTTM - Use timezone-adjusted time
                                TransNumber = PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                                TransSeq = "00000", // Placeholder - will be updated after grouping
                                RegisterID = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),

                                // Store Information
                                PolledStore = polledStoreInt,
                                PollCen = pollCen,
                                PollDate = pollDate,
                                CreateCen = createCen,
                                CreateDate = createDate,
                                CreateTime = createTime,
                                Status = " ",

                                // Tax-specific fields - all tax flags set to N per spec
                                ChargedTax1 = "N",
                                ChargedTax2 = "N",
                                ChargedTax3 = "N",
                                ChargedTax4 = "N",

                                TaxAuthCode = itemTaxAuthCode, // SLFACD from taxes.jurisdiction.region
                                TaxRateCode = lastComputedTaxRateCode, // SLFTCD - Use computed TaxRateCode from order record

                                // Tax amount in ExtendedValue and ItemSellPrice
                                ExtendedValue = FormatCurrency(itemTaxTotal.ToString("F2"), 11),
                                ExtendedValueNegativeSign = itemTaxTotal < 0 ? "-" : "",
                                ItemSellPrice = FormatCurrency(itemTaxTotal.ToString("F2"), 9),
                                SellPriceNegativeSign = itemTaxTotal < 0 ? "-" : "",

                                // Blank fields for tax line
                                SKUNumber = "000000000", // Placeholder SKU for tax line (9 zeros per validation)
                                Quantity = "000000100", // Tax quantity = 1.00 (1 * 100 in cents format)
                                QuantityNegativeSign = retailEvent.Transaction?.TransactionType == "RETURN" ? "-" : "", // SLFQTN: "-" for Return, "" for all else
                                OriginalPrice = "000000000", // SLFORG - 9 zeros for tax record
                                OriginalPriceNegativeSign = "",
                                OverridePrice = "000000000", // SLFOVR - 9 zeros when no override
                                OverridePriceNegativeSign = "", // SLFOVN - Empty string
                                OriginalRetail = "000000000", // SLFORT - 9 zeros for tax record
                                OriginalRetailNegativeSign = "",
                                ReferenceCode = "", // SLFRFC - Always empty string
                                ReferenceDesc = "", // SLFRFD - Always empty string

                                // SLFOTS, SLFOTD, SLFOTR, SLFOTT - Set based on transaction type
                                OriginalTxStore = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "00000" :
                                                 (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadOrTruncate(retailEvent.BusinessContext?.Store?.StoreId, 5) : null),
                                OriginalTxDate = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "000000" :
                                                (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? transactionDateTime.ToString("yyMMdd") : null),
                                OriginalTxRegister = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "000" :
                                                    (mappedTransactionTypeSLFTTP == "11" || mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadOrTruncate(retailEvent.BusinessContext?.Workstation?.RegisterId, 3) : null),
                                OriginalTxNumber = mappedTransactionTypeSLFTTP == "01" || mappedTransactionTypeSLFTTP == "04" || mappedTransactionTypeSLFTTP == "43" ? "00000" :
                                                  (mappedTransactionTypeSLFTTP == "87" || mappedTransactionTypeSLFTTP == "88" ? PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5) :
                                                  (mappedTransactionTypeSLFTTP == "11" && retailEvent.References?.SourceTransactionId != null ? PadOrTruncate(retailEvent.References.SourceTransactionId, 5) :
                                                  PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5))),

                                CustomerName = "",
                                CustomerNumber = "",
                                ZipCode = "         0", // SLFZIP - 9 spaces + 0 for tax records
                                Clerk = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 5),
                                EmployeeCardNumber = 0,
                                UPCCode = "0000000000000", // SLFUPC - 13 zeros
                                EReceiptEmail = "",
                                ReasonCode = "",
                                TaxExemptId1 = "", // SLFTE1 - Always empty
                                TaxExemptId2 = "", // SLFTE2 - Always empty
                                TaxExemptionName = "", // SLFTEN - Always empty
                                AdCode = "0000", // SLFADC - Always "0000" for tax records
                                AdPrice = "000000000", // SLFADP - Always "000000000" for tax records
                                AdPriceNegativeSign = "", // SLFADN - Empty string for tax records
                                PriceVehicleCode = "", // SLFPVC - Empty string for tax records
                                PriceVehicleReference = "", // SLFREF - Empty string for tax records

                                // Required fields with fixed values - per validation spec
                                OriginalSalesperson = "00000", // SLFOSP - Required
                                OriginalStore = "00000", // SLFOST - Required
                                GroupDiscAmount = "000000000", // SLFGDA - Required
                                GroupDiscSign = "", // SLFGDS - Empty string
                                SalesPerson = salesPersonId, // SLFSPS - SCO uses register ID, ACO uses "00000"
                                DiscountAmount = "000000000", // SLFDSA - Must be "000000000"
                                DiscountType = "", // SLFDST - Empty string
                                DiscountAmountNegativeSign = "", // SLFDSN - Empty string

                                GroupDiscReason = "00",
                                RegDiscReason = "00",
                                OrderNumber = "",
                                ProjectNumber = "",
                                SalesStore = 0,
                                InvStore = 0,
                                ItemScanned = "" // SLFSCN - Always blank for tax records
                            };

                            taxRecords.Add(taxRecord);
                        }
                    }
                }
            }
        }

            // Group records: Add all item records first, then all tax records
            // Update sequence numbers after grouping
            int sequence = 1;

            foreach (var itemRecord in itemRecords)
            {
                itemRecord.TransSeq = sequence.ToString().PadLeft(5, '0');
                recordSet.OrderRecords.Add(itemRecord);
                sequence++;
            }

            foreach (var taxRecord in taxRecords)
            {
                taxRecord.TransSeq = sequence.ToString().PadLeft(5, '0');
                recordSet.OrderRecords.Add(taxRecord);
                sequence++;
            }

            // Map ALL tenders - create TenderRecord(s) per tender
            // For CASH tenders: up to 3 records (CA amount, ZZ changeDue, PR cashRounding)
            // For non-CASH tenders: 1 record per tender
            if (retailEvent.Transaction?.Tenders != null && retailEvent.Transaction.Tenders.Count > 0)
            {
                bool cashChangeProcessed = false; // ZZ/PR records created only once per transaction

                foreach (var tender in retailEvent.Transaction.Tenders)
                {
                    // Line 1: Main tender record (all tender types)
                    var tenderRecord = CreateBaseTenderRecord(
                        transactionDateTime, mappedTransactionTypeSLFTTP, retailEvent,
                        polledStoreInt, pollCen, pollDate, createCen, createDate, createTime);

                    // TNFFCD - Fund code: Use TenderId directly from incoming ORAE data
                    tenderRecord.FundCode = tender.TenderId ?? "";

                    // Tender amount with sign
                    if (tender.Amount?.Value != null)
                    {
                        var (amount, sign) = FormatCurrencyWithSign(tender.Amount.Value, 11);
                        tenderRecord.Amount = amount;
                        tenderRecord.AmountNegativeSign = sign;
                    }

                    // Card/Payment fields - only populate for non-cash tenders
                    // Cash tenders may include a card object (e.g. responseCode) but should not populate card fields
                    if (tender.Card != null && tender.Method?.ToUpper() != "CASH")
                    {
                        string cardNumber = tender.Card.Last4 ?? "";
                        tenderRecord.CreditCardNumber = cardNumber.PadLeft(19, '*');
                        tenderRecord.AuthNumber = PadOrTruncate(tender.Card.AuthCode ?? "", 6);
                        tenderRecord.MagStripeFlag = PadOrTruncate(tender.Card.Emv?.Tags?.MagStrip ?? " ", 1);
                    }

                    recordSet.TenderRecords.Add(tenderRecord);

                    // For CASH tenders: create additional CA (changeDue) and PR (cashRounding) records
                    if (tender.Method?.ToUpper() == "CASH" && !cashChangeProcessed)
                    {
                        cashChangeProcessed = true;

                        // Line 2: CA - Change returned (from transaction.totals.changeDue)
                        // Sign rule: If change is returned then always "-"
                        var changeDue = retailEvent.Transaction?.Totals?.ChangeDue;
                        if (changeDue?.Value != null && !IsZeroOrEmpty(changeDue.Value))
                        {
                            var changeRecord = CreateBaseTenderRecord(
                                transactionDateTime, mappedTransactionTypeSLFTTP, retailEvent,
                                polledStoreInt, pollCen, pollDate, createCen, createDate, createTime);
                            changeRecord.FundCode = "CA";

                            var (changeAmount, _) = FormatCurrencyWithSign(changeDue.Value, 11);
                            changeRecord.Amount = changeAmount;
                            changeRecord.AmountNegativeSign = "-"; // Always negative for change returned

                            recordSet.TenderRecords.Add(changeRecord);
                        }

                        // Line 3: PR - Penny rounding (from transaction.totals.cashRounding)
                        // Sign rule: INVERTED - ORAE positive → "-", ORAE negative → blank
                        var cashRounding = retailEvent.Transaction?.Totals?.CashRounding;
                        if (cashRounding?.Value != null && !IsZeroOrEmpty(cashRounding.Value))
                        {
                            var prRecord = CreateBaseTenderRecord(
                                transactionDateTime, mappedTransactionTypeSLFTTP, retailEvent,
                                polledStoreInt, pollCen, pollDate, createCen, createDate, createTime);
                            prRecord.FundCode = "PR";

                            var (prAmount, prSign) = FormatCurrencyWithSign(cashRounding.Value, 11);
                            prRecord.Amount = prAmount;
                            // Invert sign: ORAE positive → "-", ORAE negative → blank
                            prRecord.AmountNegativeSign = (prSign == "-") ? "" : "-";

                            recordSet.TenderRecords.Add(prRecord);
                        }
                    }
                }
            }
            else if (retailEvent.Transaction?.Totals?.Net?.Value != null)
            {
                // Fallback: create one tender record with net total if no tenders array
                var tenderRecord = CreateBaseTenderRecord(
                    transactionDateTime, mappedTransactionTypeSLFTTP, retailEvent,
                    polledStoreInt, pollCen, pollDate, createCen, createDate, createTime);
                tenderRecord.FundCode = "CA"; // Default cash

                var (amount, sign) = FormatCurrencyWithSign(retailEvent.Transaction.Totals.Net.Value, 11);
                tenderRecord.Amount = amount;
                tenderRecord.AmountNegativeSign = sign;

                recordSet.TenderRecords.Add(tenderRecord);
            }

            // Update all tender record sequences (continuing from last OrderRecord sequence)
            foreach (var tenderRecord in recordSet.TenderRecords)
            {
                tenderRecord.TransactionSeq = sequence.ToString().PadLeft(5, '0');
                sequence++;
            }

            return recordSet;
        }

        // Apply timezone adjustment using IANA timeZone from ORAE store data.
        // Falls back to legacy store-ID heuristic when timeZone is absent.
        private DateTime ApplyTimezoneAdjustment(DateTime occurredAt, string? timeZone, string? storeId)
        {
            // Prefer IANA timezone from ORAE payload (e.g. "America/Toronto")
            if (!string.IsNullOrEmpty(timeZone))
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
                    // occurredAt is UTC — convert to the store's local time (handles DST automatically)
                    return TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc), tz);
                }
                catch (TimeZoneNotFoundException)
                {
                    SimpleLogger.LogWarning($"⚠ ApplyTimezoneAdjustment: unrecognised timeZone '{timeZone}', falling back to store ID heuristic");
                }
                catch (InvalidTimeZoneException)
                {
                    SimpleLogger.LogWarning($"⚠ ApplyTimezoneAdjustment: invalid timeZone data '{timeZone}', falling back to store ID heuristic");
                }
            }

            // Legacy fallback: fixed offset based on store ID ranges
            if (string.IsNullOrEmpty(storeId))
            {
                return occurredAt.AddHours(-5); // Default to eastern
            }

            bool isWesternRegion = IsWesternRegionStore(storeId);
            int hoursOffset = isWesternRegion ? -8 : -5;
            return occurredAt.AddHours(hoursOffset);
        }

        // Check if transaction has employee discount
        // Employee discounts are indicated by priceVehicle containing "EMP"
        private bool HasEmployeeDiscount(RetailEvent retailEvent)
        {
            if (retailEvent.Transaction?.Items != null)
            {
                foreach (var item in retailEvent.Transaction.Items)
                {
                    // Check if priceVehicle contains "EMP" (case-insensitive)
                    if (!string.IsNullOrEmpty(item.Pricing?.PriceVehicle) &&
                        item.Pricing.PriceVehicle.ToUpperInvariant().Contains("EMP"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Check if transaction has gift card tender
        private bool HasGiftCardTender(RetailEvent retailEvent)
        {
            if (retailEvent.Transaction?.Tenders != null)
            {
                foreach (var tender in retailEvent.Transaction.Tenders)
                {
                    if (tender.Method == "GIFT_CARD")
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Get customer ID from transaction
        // Currently returns empty as Transaction doesn't have Customer property
        // Ready for future enhancement when customer data is available
        private string GetCustomerId(RetailEvent retailEvent)
        {
            return retailEvent.Actor?.Customer?.CustomerIdToken ?? "";
        }

        // Determine if store is in western region (BC, AB, SK, MB) based on store ID
        private bool IsWesternRegionStore(string storeId)
        {
            if (!int.TryParse(storeId, out int storeNum))
            {
                return false;
            }

            // Western region stores (BC, AB, SK, MB) based on CSV data:
            // BC/AB/SK/MB stores: 170, 286, 384, 489, 103-104, 611-647, 61XXX, 62450, 63XXX, 64670, 65950
            // Plus specific 82XXX and 83XXX stores in western provinces

            // Single store IDs
            if (storeNum == 170 || storeNum == 286 || storeNum == 384 || storeNum == 489 ||
                storeNum == 103 || storeNum == 104)
            {
                return true;
            }

            // 6XX range (AB stores)
            if (storeNum >= 611 && storeNum <= 647)
            {
                return true;
            }

            // 61XXX range (BC stores)
            if (storeNum >= 61000 && storeNum <= 61999)
            {
                return true;
            }

            // 62450 (AB), 63XXX (SK), 64670 (MB), 65950 (BC)
            if (storeNum == 62450 || (storeNum >= 63000 && storeNum <= 63999) ||
                storeNum == 64670 || storeNum == 65950)
            {
                return true;
            }

            // Western 82XXX and 83XXX stores
            if (storeNum == 82952 || storeNum == 82953 || storeNum == 88007 ||
                storeNum == 83059 || storeNum == 83105 || storeNum == 83158 || storeNum == 83211 ||
                storeNum == 83230 || storeNum == 83309 || storeNum == 83313 || storeNum == 83318 ||
                storeNum == 83706 || storeNum == 83714 || storeNum == 83323 || storeNum == 83330 ||
                storeNum == 83702 || storeNum == 83704 || storeNum == 83285 || storeNum == 83718 ||
                storeNum == 83163 || storeNum == 83208)
            {
                return true;
            }

            // All other stores are eastern region (ON, QC)
            return false;
        }

       // Get date as integer in YYMMDD format
        private int GetDateAsInt(DateTime date)
        {
            return int.Parse(date.ToString("yyMMdd"));
        }

        // Get time as integer in HHMMSS format
        private int GetTimeAsInt(DateTime date)
        {
            return int.Parse(date.ToString("HHmmss"));
        }

        // Create a TenderRecord with all common fields populated.
        // Caller sets FundCode, Amount, AmountNegativeSign, and card fields (if applicable).
        private TenderRecord CreateBaseTenderRecord(
            DateTime transactionDateTime,
            string mappedTransactionTypeSLFTTP,
            RetailEvent retailEvent,
            int? polledStoreInt, int pollCen, int pollDate,
            int createCen, int createDate, int createTime)
        {
            var rec = new TenderRecord
            {
                TransactionDate = transactionDateTime.ToString("yyMMdd"),
                TransactionTime = transactionDateTime.ToString("HHmmss"),
                TransactionType = mappedTransactionTypeSLFTTP,
                TransactionNumber = PadNumeric(retailEvent.BusinessContext?.Workstation?.SequenceNumber?.ToString(), 5),
                TransactionSeq = "00000", // Placeholder - updated after grouping
                RegisterID = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 3),
                PolledStore = polledStoreInt,
                PollCen = pollCen,
                PollDate = pollDate,
                CreateCen = createCen,
                CreateDate = createDate,
                CreateTime = createTime,
                Status = " "
            };

            // Reference fields - always blank
            rec.ReferenceCode = "";
            rec.ReferenceDesc = "";

            // Card fields - default empty (overridden by caller for card tenders)
            rec.CreditCardNumber = PadOrTruncate("", 19);
            rec.AuthNumber = PadOrTruncate("", 6);
            rec.MagStripeFlag = " ";
            rec.CardExpirationDate = "0000";
            rec.PaymentHashValue = "";

            // Customer/Clerk fields
            rec.CustomerMember = "";
            rec.PostalCode = "";
            rec.Clerk = PadNumeric(retailEvent.BusinessContext?.Workstation?.RegisterId, 5);

            // Employee sale ID
            rec.EmployeeSaleId = (mappedTransactionTypeSLFTTP == "04") ? "#####" : "";

            // Email
            rec.EReceiptEmail = "";

            // Blank fields
            rec.SalesStore = 0;
            rec.InvStore = 0;
            rec.OriginalTransNumber = "";
            rec.CustomerType = " ";
            rec.OrderNumber = "";
            rec.ProjectNumber = "";

            return rec;
        }

        // Parse item-level taxes and map to OrderRecord tax fields
        private string ParseItemTaxes(TransactionItem item, OrderRecord orderRecord, RetailEvent retailEvent)
        {
            // Detect province/state for tax logic
            string? province = GetProvince(retailEvent);
            bool isOntario = province?.ToUpper() == "ON" || province?.ToUpper() == "ONTARIO";

            // Tax flags - default to N
            orderRecord.ChargedTax1 = "N";
            orderRecord.ChargedTax2 = "N";
            orderRecord.ChargedTax3 = "N";
            orderRecord.ChargedTax4 = "N";

            // Check if item has item-level taxes
            if (item.Taxes != null && item.Taxes.Count > 0)
            {
                // Process each tax detail
                foreach (var tax in item.Taxes)
                {
                    // Skip if tax is exempt
                    if (tax.TaxExempt == true)
                    {
                        continue;
                    }

                    // Check if tax amount is greater than zero
                    decimal taxAmount = 0;
                    if (tax.TaxAmount?.Value != null)
                    {
                        decimal.TryParse(tax.TaxAmount.Value, out taxAmount);
                    }

                    if (taxAmount <= 0)
                        continue;

                    // Map tax type/category to ChargedTax1-4 flags
                    string? taxType = tax.TaxType?.ToUpper() ?? tax.TaxCategory?.ToUpper();

                    // Ontario-specific logic:
                    // SLFTX3 = Y for HST (13%)
                    // SLFTX4 = Y for Partial HST / GST (5%)
                    if (isOntario)
                    {
                        // Determine if this is full HST (13%) or Partial HST (5%) based on rate
                        bool isFullHst = true; // Default to full HST

                        // Check by ratePercent
                        if (!string.IsNullOrEmpty(tax.RatePercent) && decimal.TryParse(tax.RatePercent, out decimal ratePercent))
                        {
                            isFullHst = ratePercent >= 10; // 13% is HST, 5% is Partial HST
                        }
                        // Check by taxRate (decimal form)
                        else if (tax.TaxRate != null)
                        {
                            isFullHst = tax.TaxRate.Value >= 0.10m; // 0.13 is HST, 0.05 is Partial HST
                        }
                        // Check by jurisdiction region
                        else if (!string.IsNullOrEmpty(tax.Jurisdiction?.Region))
                        {
                            string region = tax.Jurisdiction.Region.ToUpper();
                            isFullHst = region == "HON" || region.Contains("HST");
                            // HON1 indicates Partial HST
                            if (region == "HON1") isFullHst = false;
                        }

                        if (isFullHst)
                        {
                            // Full HST (13%) -> SLFTX3 = Y
                            orderRecord.ChargedTax3 = "Y";
                            orderRecord.ChargedTax4 = "N";
                        }
                        else
                        {
                            // Partial HST / GST (5%) -> SLFTX4 = Y
                            orderRecord.ChargedTax3 = "N";
                            orderRecord.ChargedTax4 = "Y";
                        }
                    }
                    else
                    {
                        // Non-Ontario provinces: use original logic
                        switch (taxType)
                        {
                            case "PST":
                            case "PROVINCIAL":
                            case "STATE":
                                orderRecord.ChargedTax1 = "Y";
                                break;
                            case "GST":
                            case "FEDERAL":
                            case "VAT":
                            case "NATIONAL":
                                orderRecord.ChargedTax2 = "Y";
                                break;
                            case "HST":
                            case "HARMONIZED":
                                // HST is combined PST+GST for non-Ontario
                                orderRecord.ChargedTax1 = "Y";
                                orderRecord.ChargedTax2 = "Y";
                                break;
                            case "QST":
                            case "QUEBEC":
                                orderRecord.ChargedTax1 = "Y";
                                break;
                            case "MUNICIPAL":
                            case "LOCAL":
                            case "CITY":
                                orderRecord.ChargedTax4 = "Y";
                                break;
                            default:
                                // Default unrecognized tax types to GST (Tax2)
                                orderRecord.ChargedTax2 = "Y";
                                break;
                        }
                    }

                    // Set tax authority code from first tax with authority
                    if (string.IsNullOrEmpty(orderRecord.TaxAuthCode) && !string.IsNullOrEmpty(tax.TaxAuthority))
                    {
                        orderRecord.TaxAuthCode = PadOrTruncate(tax.TaxAuthority, 6);
                    }

                    // Set tax rate code from first tax with code
                    if (string.IsNullOrEmpty(orderRecord.TaxRateCode) && !string.IsNullOrEmpty(tax.TaxCode))
                    {
                        orderRecord.TaxRateCode = PadOrTruncate(tax.TaxCode, 6);
                    }
                }

                // If no tax authority/rate codes found in item taxes, use store tax area
                if (string.IsNullOrEmpty(orderRecord.TaxAuthCode) &&
                    !string.IsNullOrEmpty(retailEvent.BusinessContext?.Store?.TaxArea))
                {
                    orderRecord.TaxAuthCode = PadOrTruncate(retailEvent.BusinessContext.Store.TaxArea, 6);
                }

                if (string.IsNullOrEmpty(orderRecord.TaxRateCode) &&
                    !string.IsNullOrEmpty(retailEvent.BusinessContext?.Store?.TaxArea))
                {
                    orderRecord.TaxRateCode = PadOrTruncate(retailEvent.BusinessContext.Store.TaxArea, 6);
                }
            }
            else
            {
                // Fallback: Use transaction-level tax if no item-level taxes
                if (retailEvent.Transaction?.Totals?.Tax?.Value != null &&
                    decimal.TryParse(retailEvent.Transaction.Totals.Tax.Value, out decimal taxAmount) &&
                    taxAmount > 0)
                {
                    // Ontario: SLFTX2=N, SLFTX3=Y
                    if (isOntario)
                    {
                        orderRecord.ChargedTax2 = "N";
                        orderRecord.ChargedTax3 = "Y";
                    }
                    else
                    {
                        orderRecord.ChargedTax2 = "Y"; // Default to GST (Tax2)
                    }

                    // Tax authority and rate code from store's tax area
                    if (!string.IsNullOrEmpty(retailEvent.BusinessContext?.Store?.TaxArea))
                    {
                        orderRecord.TaxAuthCode = PadOrTruncate(retailEvent.BusinessContext.Store.TaxArea, 6);
                        orderRecord.TaxRateCode = PadOrTruncate(retailEvent.BusinessContext.Store.TaxArea, 6);
                    }
                }
            }

            // SLFTCD on tax records: use taxCode from the first tax that has a jurisdiction
            string taxRateCodeForTaxRecords = "";
            if (item.Taxes != null)
            {
                foreach (var tax in item.Taxes)
                {
                    if (tax.Jurisdiction != null && !string.IsNullOrEmpty(tax.TaxCode))
                    {
                        taxRateCodeForTaxRecords = PadOrTruncate(tax.TaxCode, 6) ?? "";
                        break;
                    }
                }
            }

            // SLFTCD - TaxRateCode should always be blank for order records
            orderRecord.TaxRateCode = "";

            // SLFACD - TaxAuthCode should always be blank for order records
            orderRecord.TaxAuthCode = "";

            return taxRateCodeForTaxRecords;
        }

        // Helper method to format currency values to fixed-length strings
        private string FormatCurrency(string? value, int length = 9)
        {
            if (string.IsNullOrEmpty(value))
                return ""; // Return empty string for blank currency fields

            // Parse and convert to cents (remove decimal point)
            if (decimal.TryParse(value, out decimal amount))
            {
                long cents = (long)Math.Round(amount * 100, MidpointRounding.AwayFromZero);
                return cents.ToString().PadLeft(length, '0');
            }

            SimpleLogger.LogWarning($"⚠ FormatCurrency: unable to parse '{value}' as decimal, returning empty");
            return ""; // Default to empty string if parse fails
        }

        // Helper method to format currency with separate sign field
        private (string amount, string sign) FormatCurrencyWithSign(string? value, int length = 9)
        {
            if (string.IsNullOrEmpty(value))
            {
                return ("", ""); // Return empty string for amount and sign
            }

            if (decimal.TryParse(value, out decimal amount))
            {
                bool isNegative = amount < 0;
                long cents = (long)Math.Round(Math.Abs(amount) * 100, MidpointRounding.AwayFromZero);
                string formattedAmount = cents.ToString().PadLeft(length, '0');
                string sign = isNegative ? "-" : ""; // Use empty string for positive/no sign
                return (formattedAmount, sign);
            }

            SimpleLogger.LogWarning($"⚠ FormatCurrencyWithSign: unable to parse '{value}' as decimal, returning empty");
            return ("", ""); // Default to empty strings
        }

        // Check if a currency value string is zero, empty, or null
        private bool IsZeroOrEmpty(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return true;

            if (decimal.TryParse(value, out decimal amount))
                return amount == 0m;

            SimpleLogger.LogWarning($"⚠ IsZeroOrEmpty: unable to parse '{value}' as decimal, treating as zero");
            return true;
        }

        // Helper method to pad or truncate string to exact required length
        private string? PadOrTruncate(string? value, int length)
        {
            if (string.IsNullOrEmpty(value))
            {
                return ""; // Return empty string for blank fields
            }

            if (value.Length > length)
            {
                return value.Substring(0, length); // Truncate if too long
            }

            if (value.Length < length)
            {
                return value.PadRight(length, ' '); // Pad with spaces if too short
            }

            return value; // Exact length
        }

        // Helper method to pad numeric fields with leading zeros
        private string? PadNumeric(string? value, int length)
        {
            if (string.IsNullOrEmpty(value))
            {
                return ""; // Return empty string for blank numeric fields
            }

            if (value.Length > length)
            {
                return value.Substring(0, length); // Truncate if too long
            }

            if (value.Length < length)
            {
                return value.PadLeft(length, '0'); // Pad with leading zeros if too short
            }

            return value; // Exact length
        }

        // Log a warning and return a default value (used in switch expressions)
        private static string LogAndDefault(string message, string defaultValue)
        {
            SimpleLogger.LogWarning(message);
            return defaultValue;
        }

        // Get province/state from retail event (for tax logic)
        private string? GetProvince(RetailEvent retailEvent)
        {
            // Try to extract province from tax area code
            // Common patterns: "ON" for Ontario, "QC" for Quebec, etc.
            string? taxArea = retailEvent.BusinessContext?.Store?.TaxArea;

            if (!string.IsNullOrEmpty(taxArea))
            {
                // If tax area starts with province code, extract it
                if (taxArea.Length >= 2)
                {
                    string prefix = taxArea.Substring(0, 2).ToUpper();

                    // Canadian provinces
                    if (prefix == "ON" || prefix == "QC" || prefix == "BC" ||
                        prefix == "AB" || prefix == "MB" || prefix == "SK" ||
                        prefix == "NS" || prefix == "NB" || prefix == "PE" ||
                        prefix == "NL" || prefix == "YT" || prefix == "NT" || prefix == "NU")
                    {
                        return prefix;
                    }
                }

                // Check if tax area contains province name
                string taxAreaUpper = taxArea.ToUpper();
                if (taxAreaUpper.Contains("ONTARIO") || taxAreaUpper.Contains("HON"))
                    return "ON";
                if (taxAreaUpper.Contains("QUEBEC"))
                    return "QC";
                if (taxAreaUpper.Contains("BRITISH") || taxAreaUpper.Contains("BC"))
                    return "BC";
            }

            // TODO: Add province field to Store class for explicit province detection
            // For now, return null if unable to detect
            return null;
        }

        // Get the EPP coverage identifier value from an EPP item
        private string? GetEPPCoverageIdentifier(TransactionItem item)
        {
            if (item.Attributes != null &&
                item.Attributes.TryGetValue("x-epp-coverage-identifier", out string? value))
            {
                return value;
            }
            return null;
        }

        // Determine tax authority code based on tax rate and jurisdiction
        private string DetermineTaxAuthority(RetailEvent retailEvent, decimal totalTax)
        {
            // Check item-level taxes for rate information
            if (retailEvent.Transaction?.Items != null)
            {
                foreach (var item in retailEvent.Transaction.Items)
                {
                    if (item.Taxes != null)
                    {
                        foreach (var tax in item.Taxes)
                        {
                            if (tax.TaxRate != null)
                            {
                                decimal rate = tax.TaxRate.Value;

                                // HST is 13% in Ontario
                                if (Math.Abs(rate - 0.13m) < 0.001m)
                                    return "HON"; // 13% HST

                                // GST is 5% federal
                                if (Math.Abs(rate - 0.05m) < 0.001m)
                                    return "HON1"; // 5% GST
                            }

                            // Check tax authority from tax detail
                            if (!string.IsNullOrEmpty(tax.TaxAuthority))
                                return PadOrTruncate(tax.TaxAuthority, 6) ?? "HON";
                        }
                    }
                }
            }

            // Calculate effective tax rate from totals
            // Calculate subtotal as: Net - Tax (pre-tax amount)
            if (retailEvent.Transaction?.Totals?.Net?.Value != null &&
                decimal.TryParse(retailEvent.Transaction.Totals.Net.Value, out decimal netAmount))
            {
                decimal subtotal = netAmount - totalTax;

                if (subtotal > 0)
                {
                    decimal effectiveRate = totalTax / subtotal;

                    // Check if rate is close to 13% (HST)
                    if (Math.Abs(effectiveRate - 0.13m) < 0.01m)
                        return "HON"; // ~13% = HST
                    // Check if rate is close to 5% (GST)
                    else if (Math.Abs(effectiveRate - 0.05m) < 0.01m)
                        return "HON1"; // ~5% = GST
                }
            }
            // Alternative: Calculate subtotal as Gross - Discounts
            else if (retailEvent.Transaction?.Totals?.Gross?.Value != null &&
                     decimal.TryParse(retailEvent.Transaction.Totals.Gross.Value, out decimal grossAmount))
            {
                decimal discountAmount = 0;
                if (retailEvent.Transaction?.Totals?.Discounts?.Value != null)
                {
                    decimal.TryParse(retailEvent.Transaction.Totals.Discounts.Value, out discountAmount);
                }

                decimal subtotal = grossAmount - discountAmount;

                if (subtotal > 0)
                {
                    decimal effectiveRate = totalTax / subtotal;

                    // Check if rate is close to 13% (HST)
                    if (Math.Abs(effectiveRate - 0.13m) < 0.01m)
                        return "HON"; // ~13% = HST
                    // Check if rate is close to 5% (GST)
                    else if (Math.Abs(effectiveRate - 0.05m) < 0.01m)
                        return "HON1"; // ~5% = GST
                }
            }

            // Check province to determine default
            string? province = GetProvince(retailEvent);
            if (province?.ToUpper() == "ON" || province?.ToUpper() == "ONTARIO")
                return "HON"; // Ontario defaults to HST

            // Default to HON for unknown cases
            return "HON";
        }

        // Map Tax Authority Code to Sales Transaction Line Type (SLFLNT) for tax records
        // Based on TBLFLD TAXTXC mapping table
        private string MapTaxAuthToLineType(string taxAuthCode)
        {
            string code = taxAuthCode.Trim().ToUpper();
            return code switch
            {
                "BC"   => "XR", // BC PST
                "FED"  => "XG", // Goods and Services Tax (GST)
                "HNB"  => "XN", // HST New Brunswick
                "HNF"  => "XF", // HST Newfoundland
                "HNS"  => "XV", // HST Nova Scotia
                "HON"  => "XH", // HST Ontario
                "HON1" => "XI", // HST Partial Ontario
                "HPE"  => "XP", // HST Prince Edward Island
                "MB"   => "XM", // Manitoba PST
                "PQ"   => "XQ", // Quebec Provincial Tax
                "SK"   => "XS", // Saskatchewan PST
                _      => "XH"  // Default to HST Ontario
            };
        }

        private string MapTransTypeSLFTTP(string input, bool hasEmployeeDiscount)
        {
            // Employee sales get SLFTTP = "04" (when SLFPVC = EMP)
            if (input == "SALE" && hasEmployeeDiscount)
                return "04";

            return input switch
            {
                "SALE" => "01",          // Regular sale (no employee discount)
                "RETURN" => "11",        // Return transactions
                "AR_PAYMENT" => "43",    // AR Payment
                "VOID" => "87",          // Current transaction void
                "POST_VOID" => "88",     // Post void transaction
                _ => LogAndDefault($"⚠ MapTransTypeSLFTTP: unrecognised transactionType '{input}', defaulting to '01'", "01")
            };
        }

        private string MapTransTypeSLFLNT(string input, bool hasEmployeeDiscount, bool hasGiftCardTender, bool hasCustomerId)
        {
            // Handle SALE transactions (SLFTTP = 01 or 04)
            if (input == "SALE")
            {
                // Employee sales (PVCode = EMP) → SLFLNT = "04"
                if (hasEmployeeDiscount)
                    return "04";

                // Gift card activation → SLFLNT = "45"
                if (hasGiftCardTender)
                    return "45";

                // Regular trade (Customer ID exists) → SLFLNT = "02"
                if (hasCustomerId)
                    return "02";

                // Regular sales (no Customer ID) → SLFLNT = "01"
                return "01";
            }

            // Handle RETURN transactions (SLFTTP = 11)
            if (input == "RETURN")
            {
                // Return in gift card → SLFLNT = "45"
                if (hasGiftCardTender)
                    return "45";

                // Trade return (Customer ID exists) → SLFLNT = "12"
                if (hasCustomerId)
                    return "12";

                // Regular return (no Customer ID) → SLFLNT = "11"
                return "11";
            }

            // Current transaction void (SLFTTP = 87) → SLFLNT = "87"
            if (input == "VOID")
                return "87";

            // Post void transaction (SLFTTP = 88) → SLFLNT = "01"
            if (input == "POST_VOID")
                return "01";

            // Default to regular sale
            SimpleLogger.LogWarning($"⚠ MapTransTypeSLFLNT: unrecognised transactionType '{input}', defaulting to '01'");
            return "01";
    }


    public RetailEvent ReadRecordSetFromString(string datain)
    {
        try
        {
            string jsonContent = datain;
            var retailEvent = JsonSerializer.Deserialize<RetailEvent>(jsonContent);
            // Fix for CS9035: Always set required Transaction property
            return retailEvent ?? new RetailEvent { Transaction = new Transaction { Items = new List<TransactionItem>(), Totals = new Totals() } };
        }
        catch (JsonException ex)
        {
            SimpleLogger.LogError($"JSON parsing error: {ex.Message}");
            throw;
        }
    }
}
