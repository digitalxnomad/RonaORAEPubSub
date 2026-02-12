using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json.Serialization;
using PubSubApp.Models;

namespace PubSubApp.Validation;

// RecordSet Output Validation (RIMSLF/RIMTNF)
public static class RecordSetValidator
{
    public static List<string> ValidateRecordSetOutput(RecordSet recordSet)
    {
        var errors = new List<string>();

        if (recordSet == null)
        {
            errors.Add("RecordSet is null");
            return errors;
        }

        // Validate OrderRecords (RIMSLF)
        if (recordSet.OrderRecords != null)
        {
            for (int i = 0; i < recordSet.OrderRecords.Count; i++)
            {
                var order = recordSet.OrderRecords[i];
                string prefix = $"RIMSLF[{i}]";

                // Required fields
                if (string.IsNullOrEmpty(order.TransType))
                    errors.Add($"{prefix}: SLFTTP (TransType) is required");
                if (string.IsNullOrEmpty(order.LineType))
                    errors.Add($"{prefix}: SLFLNT (LineType) is required");
                if (string.IsNullOrEmpty(order.TransDate))
                    errors.Add($"{prefix}: SLFTDT (TransDate) is required");
                if (string.IsNullOrEmpty(order.TransTime))
                    errors.Add($"{prefix}: SLFTTM (TransTime) is required");
                if (string.IsNullOrEmpty(order.TransNumber))
                    errors.Add($"{prefix}: SLFTTX (TransNumber) is required");
                if (string.IsNullOrEmpty(order.TransSeq))
                    errors.Add($"{prefix}: SLFTSQ (TransSeq) is required");
                if (string.IsNullOrEmpty(order.RegisterID))
                    errors.Add($"{prefix}: SLFREG (RegisterID) is required");
                if (string.IsNullOrEmpty(order.SKUNumber))
                    errors.Add($"{prefix}: SLFSKU (SKUNumber) is required");
                if (order.PolledStore == 0)
                    errors.Add($"{prefix}: SLFPST (PolledStore) is required");
                // PollCen and CreateCen are century digits (0-9), so 0 is valid for 2000-2099
                if (order.PollCen < 0 || order.PollCen > 9)
                    errors.Add($"{prefix}: SLFPCN (PollCen) must be 0-9");
                if (order.PollDate == 0)
                    errors.Add($"{prefix}: SLFPDT (PollDate) is required");
                if (order.CreateCen < 0 || order.CreateCen > 9)
                    errors.Add($"{prefix}: SLFCCN (CreateCen) must be 0-9");
                if (order.CreateDate == 0)
                    errors.Add($"{prefix}: SLFCDT (CreateDate) is required");
                if (order.CreateTime == 0)
                    errors.Add($"{prefix}: SLFCTM (CreateTime) is required");

                // Pricing fields
                if (string.IsNullOrEmpty(order.Quantity))
                    errors.Add($"{prefix}: SLFQTY (Quantity) is required");
                if (string.IsNullOrEmpty(order.ItemSellPrice))
                    errors.Add($"{prefix}: SLFSEL (ItemSellPrice) is required");
                if (string.IsNullOrEmpty(order.ExtendedValue))
                    errors.Add($"{prefix}: SLFEXT (ExtendedValue) is required");

                // Validate all string field lengths using reflection
                ValidateStringFieldLengths(order, typeof(OrderRecord), prefix, errors);
            }
        }

        // Validate TenderRecords (RIMTNF)
        if (recordSet.TenderRecords != null)
        {
            for (int i = 0; i < recordSet.TenderRecords.Count; i++)
            {
                var tender = recordSet.TenderRecords[i];
                string prefix = $"RIMTNF[{i}]";

                // Required fields
                if (string.IsNullOrEmpty(tender.TransactionType))
                    errors.Add($"{prefix}: TNFTTP (TransactionType) is required");
                if (string.IsNullOrEmpty(tender.TransactionDate))
                    errors.Add($"{prefix}: TNFTDT (TransactionDate) is required");
                if (string.IsNullOrEmpty(tender.TransactionTime))
                    errors.Add($"{prefix}: TNFTTM (TransactionTime) is required");
                if (string.IsNullOrEmpty(tender.TransactionNumber))
                    errors.Add($"{prefix}: TNFTTX (TransactionNumber) is required");
                if (string.IsNullOrEmpty(tender.TransactionSeq))
                    errors.Add($"{prefix}: TNFTSQ (TransactionSeq) is required");
                if (string.IsNullOrEmpty(tender.RegisterID))
                    errors.Add($"{prefix}: TNFREG (RegisterID) is required");
                if (string.IsNullOrEmpty(tender.FundCode))
                    errors.Add($"{prefix}: TNFFCD (FundCode) is required");
                if (string.IsNullOrEmpty(tender.Amount))
                    errors.Add($"{prefix}: TNFAMT (Amount) is required");
                if (tender.PolledStore == 0)
                    errors.Add($"{prefix}: TNFPST (PolledStore) is required");
                // PollCen and CreateCen are century digits (0-9), so 0 is valid for 2000-2099
                if (tender.PollCen < 0 || tender.PollCen > 9)
                    errors.Add($"{prefix}: TNFPCN (PollCen) must be 0-9");
                if (tender.PollDate == 0)
                    errors.Add($"{prefix}: TNFPDT (PollDate) is required");
                if (tender.CreateCen < 0 || tender.CreateCen > 9)
                    errors.Add($"{prefix}: TNFCCN (CreateCen) must be 0-9");
                if (tender.CreateDate == 0)
                    errors.Add($"{prefix}: TNFCDT (CreateDate) is required");
                if (tender.CreateTime == 0)
                    errors.Add($"{prefix}: TNFCTM (CreateTime) is required");

                // Validate all string field lengths using reflection
                ValidateStringFieldLengths(tender, typeof(TenderRecord), prefix, errors);
            }
        }

        // Warn if both are empty
        if ((recordSet.OrderRecords == null || recordSet.OrderRecords.Count == 0) &&
            (recordSet.TenderRecords == null || recordSet.TenderRecords.Count == 0))
        {
            errors.Add("RecordSet contains no OrderRecords or TenderRecords");
        }

        return errors;
    }

    // Helper method to validate string field lengths based on StringLength attributes
    private static void ValidateStringFieldLengths(object record, Type recordType, string prefix, List<string> errors)
    {
        var properties = recordType.GetProperties();

        foreach (var prop in properties)
        {
            // Only validate string properties
            if (prop.PropertyType != typeof(string))
                continue;

            var stringLengthAttr = prop.GetCustomAttributes(typeof(StringLengthAttribute), false)
                                       .FirstOrDefault() as StringLengthAttribute;

            if (stringLengthAttr == null)
                continue;

            var jsonAttr = prop.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false)
                              .FirstOrDefault() as JsonPropertyNameAttribute;
            string fieldName = jsonAttr?.Name ?? prop.Name;

            string? value = prop.GetValue(record) as string;
            int minLength = stringLengthAttr.MinimumLength;
            int maxLength = stringLengthAttr.MaximumLength;

            // Rule: For blank values, they can be zero length OR properly padded.
            //       For non-blank values, length must match exactly (MinimumLength == MaxLength due to padding)
            if (string.IsNullOrEmpty(value))
            {
                // Blank field - can be empty (length 0) or properly padded to minLength
                // No validation error for blank fields
                continue;
            }

            // Non-blank field - must match exact length (MinimumLength should equal MaxLength)
            if (value.Length < minLength)
            {
                errors.Add($"{prefix}: {fieldName} ({prop.Name}) length is {value.Length}, but minimum is {minLength}");
            }
            else if (value.Length > maxLength)
            {
                errors.Add($"{prefix}: {fieldName} ({prop.Name}) length is {value.Length}, but maximum is {maxLength}");
            }
            else if (minLength == maxLength && value.Length != minLength)
            {
                // When min==max, value must be exact length
                errors.Add($"{prefix}: {fieldName} ({prop.Name}) length is {value.Length}, but must be exactly {minLength}");
            }
        }
    }
}
