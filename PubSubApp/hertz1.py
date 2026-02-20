import csv
import os

CSV_PATH = r"C:\Users\cwils\Downloads\hertz.csv"
OUT_SQL  = r"C:\Users\cwils\Downloads\hertz_inserts.sql"

TABLE = "100181_data.location_id_config"

# Batch size for multi-row INSERT
BATCH_SIZE = 1000

def norm(s: str) -> str:
    return "".join(ch.lower() for ch in s if ch.isalnum())

# Map CSV column names -> DB column names (normalized matching)
COL_MAP = {
    norm("LocationID"): "location_id",
    norm("PC Name"): "machine_name",
    norm("Printer Type"): "printer_type",
    norm("ClientVersion"): "client_version",
}

def sql_escape(val: str) -> str:
    """Return SQL literal or NULL."""
    if val is None:
        return "NULL"
    v = val.strip()
    if v == "":
        return "NULL"
    # escape backslash and single quote for MySQL
    v = v.replace("\\", "\\\\").replace("'", "''")
    return f"'{v}'"

with open(CSV_PATH, "r", newline="", encoding="utf-8-sig") as f:
    reader = csv.DictReader(f)
    if not reader.fieldnames:
        raise SystemExit("CSV has no header row / fieldnames.")

    # Build ordered list of DB columns based on CSV header order
    db_cols = []
    csv_keys_in_order = []
    for h in reader.fieldnames:
        nh = norm(h)
        if nh in COL_MAP:
            db_cols.append(COL_MAP[nh])
            csv_keys_in_order.append(h)

    if not db_cols:
        raise SystemExit(
            "Could not map CSV headers. Expected headers like: "
            "LocationID, PC Name, Printer Type, ClientVersion"
        )

    # Ensure required columns exist (optional, but recommended)
    required = ["machine_name", "printer_type", "client_version"]
    missing_required = [c for c in required if c not in db_cols]
    if missing_required:
        raise SystemExit(f"Missing required mapped columns: {missing_required}")

    # Write SQL
    with open(OUT_SQL, "w", encoding="utf-8") as out:
        out.write("START TRANSACTION;\n")

        rows = []
        total = 0

        for row in reader:
            values = []
            for csv_key, db_col in zip(csv_keys_in_order, db_cols):
                v = row.get(csv_key, "")

                # Custom rules:
                if db_col == "location_id":
                    # blanks -> NULL
                    values.append(sql_escape(v))
                elif db_col == "client_version":
                    # if empty, force DEFAULT
                    vv = v.strip()
                    values.append(sql_escape(vv if vv else "DEFAULT"))
                else:
                    values.append(sql_escape(v))

            rows.append(f"({', '.join(values)})")
            total += 1

            if len(rows) >= BATCH_SIZE:
                out.write(
                    f"INSERT INTO {TABLE} ({', '.join(db_cols)}) VALUES\n"
                    + ",\n".join(rows)
                    + ";\n"
                )
                rows = []

        # flush remainder
        if rows:
            out.write(
                f"INSERT INTO {TABLE} ({', '.join(db_cols)}) VALUES\n"
                + ",\n".join(rows)
                + ";\n"
            )

        out.write("COMMIT;\n")

print(f"Done. Wrote {OUT_SQL}")
print(f"Rows processed: {total}")


