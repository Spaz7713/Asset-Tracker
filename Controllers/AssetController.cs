// Controllers/AssetController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MediaTracker.Data;
using MediaTracker.Models;
using ZXing;
using ZXing.Common;
using ZXing.PDF417;
using System.Drawing;
using System.IO;

namespace MediaTracker.Controllers
{
    public class AssetController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AssetController(ApplicationDbContext context)
        {
            _context = context;
        }

        // POST: Asset/GenerateLabelsForAsset
        // Creates labels for any unlabeled units of the given asset (based on asset.Quantity)
        [HttpPost]
        public async Task<IActionResult> GenerateLabelsForAsset(int assetId)
        {
            var asset = await _context.Assets.FindAsync(assetId);
            if (asset == null) return NotFound();

            var existingCount = await _context.Labels.CountAsync(l => l.AssetId == assetId);
            var toCreate = asset.Quantity - existingCount;
            if (toCreate <= 0)
            {
                TempData["Message"] = "No missing units to generate labels for.";
                return RedirectToAction(nameof(Index));
            }

            for (int i = 0; i < toCreate; i++)
            {
                var inv = $"INV-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString().Substring(0, 6).ToUpperInvariant()}";
                var label = new MediaTracker.Models.Label
                {
                    AssetId = asset.Id,
                    InventoryNumber = inv,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Add(label);
            }

            await _context.SaveChangesAsync();
            TempData["Message"] = $"Generated {toCreate} label(s) for asset '{asset.Name}'.";
            return RedirectToAction(nameof(Index));
        }

        private Bitmap GeneratePdf417(string payload)
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.PDF_417,
                Options = new EncodingOptions
                {
                    Height = 120,
                    Width = 300,
                    Margin = 10
                }
            };

            var pixelData = writer.Write(payload);
            // create bitmap from raw pixel data
            var bitmap = new Bitmap(pixelData.Width, pixelData.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, pixelData.Width, pixelData.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(pixelData.Pixels, 0, bitmapData.Scan0, pixelData.Pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }

        // GET: Asset/Labels
        public IActionResult Labels()
        {
            var assetsList = _context.Assets.AsNoTracking().ToList();
            ViewBag.Assets = assetsList;
            ViewBag.AssetCount = assetsList.Count;
            return View();
        }

        // POST: Asset/ToggleCheckOut
        // Accepts either labelId or sku identifier (sku takes precedence when provided)
        [HttpPost]
        public async Task<IActionResult> ToggleCheckOut(int? labelId, string? sku, string? user)
        {
            MediaTracker.Models.Label? label = null;

            if (!string.IsNullOrEmpty(sku))
            {
                // find existing label matching sku for the asset
                label = await _context.Labels.FirstOrDefaultAsync(l => l.InventoryNumber == sku && l.AssetId != null);
                if (label == null)
                {
                    // create a new label record for this SKU and link to the asset if possible
                    var asset = await _context.Assets.FirstOrDefaultAsync(a => a.SKU == sku);
                    label = new MediaTracker.Models.Label
                    {
                        AssetId = asset?.Id,
                        InventoryNumber = sku,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Add(label);
                    await _context.SaveChangesAsync();
                }
            }
            else if (labelId.HasValue)
            {
                label = await _context.Labels.FindAsync(labelId.Value);
                if (label == null) return NotFound();
            }
            else
            {
                return BadRequest();
            }

            // validate employee if checking out
            var employee = string.IsNullOrEmpty(user) ? null : await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == user || e.Name == user);
            if (!label.IsCheckedOut)
            {
                // attempting to check out
                if (employee == null || !employee.IsApproved)
                {
                    TempData["Error"] = "Employee not authorized to check out items.";
                    return RedirectToAction(nameof(Index));
                }

                // mark checked out and decrement quantity if possible
                label.IsCheckedOut = true;
                label.CheckedOutBy = employee.EmployeeId;
                label.CheckedOutAt = DateTime.UtcNow;

                // Do not modify asset.Quantity here — Quantity represents total units. Availability is tracked per-label.
            }
            else
            {
                // checking in
                label.IsCheckedOut = false;
                label.CheckedOutBy = null;
                label.CheckedOutAt = null;

                // Do not modify asset.Quantity here — Quantity represents total units. Availability is tracked per-label.
            }

            _context.Update(label);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: Asset/Spreadsheet
        // Shows an Excel-style editable list and a SQL textbox for executing read-only queries
        public IActionResult Spreadsheet()
        {
            var assets = _context.Assets.AsNoTracking().ToList();
            ViewBag.ApprovedEmployees = _context.Employees.AsNoTracking().Where(e => e.IsApproved).ToList();

            var allLabels = _context.Labels.AsNoTracking().Where(l => l.AssetId != null).OrderBy(l => l.Id).ToList();
            var labelGroups = allLabels.GroupBy(l => l.AssetId!.Value).ToDictionary(g => g.Key, g => g.ToList());
            ViewBag.LabelGroups = labelGroups;
            // compute derived available counts per asset
            var availableCounts = new Dictionary<int, int>();
            foreach (var a in assets)
            {
                if (labelGroups.TryGetValue(a.Id, out var labelsFor))
                {
                    availableCounts[a.Id] = labelsFor.Count(l => !l.IsCheckedOut);
                }
                else
                {
                    availableCounts[a.Id] = a.Quantity;
                }
            }
            ViewBag.AvailableCounts = availableCounts;

            return View(assets);
        }

        // POST: Asset/ExecuteQuery
        [HttpPost]
        public async Task<IActionResult> ExecuteQuery(string sql)
        {
            // Basic safety: only allow single SELECT statements
            if (string.IsNullOrWhiteSpace(sql))
            {
                TempData["Error"] = "SQL query is empty.";
                return RedirectToAction(nameof(Spreadsheet));
            }

            var trimmed = sql.Trim();
            var lowered = trimmed.ToLowerInvariant();
            if (!lowered.StartsWith("select"))
            {
                TempData["Error"] = "Only SELECT queries are allowed.";
                return RedirectToAction(nameof(Spreadsheet));
            }

            if (lowered.Contains(";") || lowered.Contains("drop") || lowered.Contains("delete") || lowered.Contains("update") || lowered.Contains("insert") || lowered.Contains("alter"))
            {
                TempData["Error"] = "Query contains disallowed keywords or multiple statements.";
                return RedirectToAction(nameof(Spreadsheet));
            }

            var conn = _context.Database.GetDbConnection();
            await using (conn)
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                using var reader = await cmd.ExecuteReaderAsync();
                var table = new System.Data.DataTable();
                table.Load(reader);
                ViewBag.QueryResult = table;
            }

            var assets = _context.Assets.AsNoTracking().ToList();
            return View("Spreadsheet", assets);
        }

        // POST: Asset/SaveSpreadsheet
        // Expects JSON array of assets with Id and edited fields. Simple batch update.
        [HttpPost]
        public async Task<IActionResult> SaveSpreadsheet([FromForm] string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return BadRequest();

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Array) return BadRequest();

            // Detect flattened label rows (we create rows with property "rowType" and "labelId")
            var enumeratorForDetection = root.EnumerateArray();
            bool isLabelRows = false;
            if (enumeratorForDetection.MoveNext())
            {
                var first = enumeratorForDetection.Current;
                isLabelRows = (first.ValueKind == System.Text.Json.JsonValueKind.Object && first.TryGetProperty("rowType", out _));
            }

            if (isLabelRows)
            {
                var assetUpdates = new System.Collections.Generic.Dictionary<int, MediaTracker.Models.Asset>();

                foreach (var el in root.EnumerateArray())
                {
                    if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

                    int assetId = 0;
                    if (el.TryGetProperty("assetId", out var pAid) && pAid.ValueKind == System.Text.Json.JsonValueKind.Number) assetId = pAid.GetInt32();

                    int? labelId = null;
                    if (el.TryGetProperty("labelId", out var pLid) && pLid.ValueKind == System.Text.Json.JsonValueKind.Number) labelId = pLid.GetInt32();

                    string inventory = "";
                    if (el.TryGetProperty("inventoryNumber", out var pInv) && pInv.ValueKind == System.Text.Json.JsonValueKind.String) inventory = pInv.GetString() ?? "";

                    bool isCheckedOut = false;
                    if (el.TryGetProperty("isCheckedOut", out var pChk) && pChk.ValueKind == System.Text.Json.JsonValueKind.True) isCheckedOut = true;

                    // Update existing label: InventoryNumber is immutable once assigned. Only update checkout state.
                    if (labelId.HasValue && labelId.Value > 0)
                    {
                        var label = await _context.Labels.FindAsync(labelId.Value);
                        if (label != null)
                        {
                            label.IsCheckedOut = isCheckedOut;
                            _context.Update(label);
                        }
                    }
                    else
                    {
                        // Create new label if inventory provided
                        if (!string.IsNullOrWhiteSpace(inventory))
                        {
                            var exists = await _context.Labels.AnyAsync(l => l.InventoryNumber == inventory);
                            if (!exists)
                            {
                                var label = new MediaTracker.Models.Label
                                {
                                    AssetId = assetId > 0 ? assetId : null,
                                    InventoryNumber = inventory,
                                    CreatedAt = DateTime.UtcNow,
                                    IsCheckedOut = isCheckedOut
                                };
                                _context.Add(label);
                            }
                        }
                    }

                    // Collect asset updates
                    if (assetId > 0)
                    {
                        if (!assetUpdates.ContainsKey(assetId)) assetUpdates[assetId] = new MediaTracker.Models.Asset { Id = assetId };
                        var a = assetUpdates[assetId];
                        if (el.TryGetProperty("name", out var pName) && pName.ValueKind == System.Text.Json.JsonValueKind.String) a.Name = pName.GetString() ?? a.Name;
                        if (el.TryGetProperty("sku", out var pSku) && pSku.ValueKind == System.Text.Json.JsonValueKind.String) a.SKU = pSku.GetString() ?? a.SKU;
                        if (el.TryGetProperty("serialNumber", out var pSn) && pSn.ValueKind == System.Text.Json.JsonValueKind.String) a.SerialNumber = pSn.GetString() ?? a.SerialNumber;
                        if (el.TryGetProperty("productKey", out var pPk) && pPk.ValueKind == System.Text.Json.JsonValueKind.String) a.ProductKey = pPk.GetString() ?? a.ProductKey;
                        if (el.TryGetProperty("location", out var pLoc) && pLoc.ValueKind == System.Text.Json.JsonValueKind.String) a.Location = pLoc.GetString() ?? a.Location;
                        if (el.TryGetProperty("quantity", out var pQty) && pQty.ValueKind == System.Text.Json.JsonValueKind.Number) a.Quantity = pQty.GetInt32();
                        if (el.TryGetProperty("status", out var pSt) && pSt.ValueKind == System.Text.Json.JsonValueKind.Number) a.Status = (MediaTracker.Models.AssetStatus)pSt.GetInt32();
                        if (el.TryGetProperty("type", out var pTy) && pTy.ValueKind == System.Text.Json.JsonValueKind.Number) a.Type = (MediaTracker.Models.AssetType)pTy.GetInt32();
                    }
                }

                // Validate required identifier fields per asset type before applying
                foreach (var kvp in assetUpdates)
                {
                    var aid = kvp.Key;
                    var upd = kvp.Value;
                    // load existing to inspect type/name
                    var existingAsset = await _context.Assets.FindAsync(aid);
                    if (existingAsset == null) continue;
                    bool hasSerialU = !string.IsNullOrWhiteSpace(upd.SerialNumber) || !string.IsNullOrWhiteSpace(existingAsset.SerialNumber);
                    bool hasProductU = !string.IsNullOrWhiteSpace(upd.ProductKey) || !string.IsNullOrWhiteSpace(existingAsset.ProductKey);
                    if (!hasSerialU && !hasProductU)
                    {
                        TempData["Error"] = $"Either a Serial number or Product key is required for asset '{existingAsset.Name}'.";
                        return RedirectToAction(nameof(Spreadsheet));
                    }
                }

                // Apply asset updates
                foreach (var kv in assetUpdates)
                {
                    var existing = await _context.Assets.FindAsync(kv.Key);
                    if (existing == null) continue;
                    var upd = kv.Value;
                    if (!string.IsNullOrEmpty(upd.Name)) existing.Name = upd.Name;
                    if (!string.IsNullOrEmpty(upd.SKU)) existing.SKU = upd.SKU;
                    if (!string.IsNullOrEmpty(upd.SerialNumber)) existing.SerialNumber = upd.SerialNumber;
                    if (!string.IsNullOrEmpty(upd.ProductKey)) existing.ProductKey = upd.ProductKey;
                    if (!string.IsNullOrEmpty(upd.Location)) existing.Location = upd.Location;
                    existing.Quantity = upd.Quantity;
                    existing.Status = upd.Status;
                    existing.Type = upd.Type;
                    existing.LastUpdated = DateTime.UtcNow;
                    _context.Update(existing);
                }

                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Spreadsheet));
            }

            // Fallback: older format - list of asset objects
            var edited = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<MediaTracker.Models.Asset>>(json);
            if (edited == null) return BadRequest();

            foreach (var e in edited)
            {
                var existing = await _context.Assets.FindAsync(e.Id);
                if (existing == null) continue;
                // Update allowed fields only
                existing.Name = e.Name;
                existing.SKU = e.SKU;
                existing.Location = e.Location;
                existing.Quantity = e.Quantity;
                existing.Status = e.Status;
                existing.Type = e.Type;
                existing.LastUpdated = DateTime.UtcNow;
                _context.Update(existing);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Spreadsheet));
        }

        // GET: Asset/ExportCsv
        public async Task<IActionResult> ExportCsv()
        {
            var assets = await _context.Assets.AsNoTracking().ToListAsync();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Id,Name,Type,SKU,SerialNumber,ProductKey,Location,Quantity,Status,LastUpdated");
            foreach (var a in assets)
            {
                // escape commas by wrapping in quotes and escaping quotes
                string Escape(string s) => s == null ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";
                sb.Append(a.Id).Append(',');
                sb.Append(Escape(a.Name)).Append(',');
                sb.Append((int)a.Type).Append(',');
                sb.Append(Escape(a.SKU)).Append(',');
                sb.Append(Escape(a.SerialNumber)).Append(',');
                sb.Append(Escape(a.ProductKey)).Append(',');
                sb.Append(Escape(a.Location)).Append(',');
                sb.Append(a.Quantity).Append(',');
                sb.Append((int)a.Status).Append(',');
                sb.Append(a.LastUpdated.ToString("o"));
                sb.AppendLine();
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", "assets.csv");
        }

        // POST: Asset/ImportCsv
        [HttpPost]
        public async Task<IActionResult> ImportCsv(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "No file selected.";
                return RedirectToAction(nameof(Spreadsheet));
            }

            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            string? header = await reader.ReadLineAsync();
            if (header == null)
            {
                TempData["Error"] = "Empty file.";
                return RedirectToAction(nameof(Spreadsheet));
            }

            var updated = 0;
            var created = 0;
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Split CSV respecting quoted values
                var cols = ParseCsvLine(line);
                // Expected columns: Id,Name,Type,SKU,Location,Quantity,Status,LastUpdated
                int.TryParse(cols.ElementAtOrDefault(0) ?? "", out var id);
                var name = cols.ElementAtOrDefault(1) ?? string.Empty;
                var typeRaw = cols.ElementAtOrDefault(2) ?? "0";
                int.TryParse(typeRaw, out var typeInt);
                var sku = cols.ElementAtOrDefault(3) ?? string.Empty;
                var location = cols.ElementAtOrDefault(4) ?? string.Empty;
                int.TryParse(cols.ElementAtOrDefault(5) ?? "0", out var qty);
                int.TryParse(cols.ElementAtOrDefault(6) ?? "0", out var statusInt);

                MediaTracker.Models.Asset asset;
                if (id > 0)
                {
                    asset = await _context.Assets.FindAsync(id) ?? new MediaTracker.Models.Asset();
                }
                else
                {
                    asset = new MediaTracker.Models.Asset();
                }

                asset.Name = name;
                asset.Type = Enum.IsDefined(typeof(MediaTracker.Models.AssetType), typeInt) ? (MediaTracker.Models.AssetType)typeInt : MediaTracker.Models.AssetType.Equipment;
                asset.SKU = sku;
                asset.Location = location;
                asset.Quantity = qty;
                asset.Status = Enum.IsDefined(typeof(MediaTracker.Models.AssetStatus), statusInt) ? (MediaTracker.Models.AssetStatus)statusInt : MediaTracker.Models.AssetStatus.Available;
                asset.LastUpdated = DateTime.UtcNow;

                if (id > 0 && await _context.Assets.AnyAsync(a => a.Id == id))
                {
                    _context.Update(asset);
                    updated++;
                }
                else
                {
                    _context.Add(asset);
                    created++;
                }
            }

            await _context.SaveChangesAsync();
            TempData["Message"] = $"Import complete. Updated={updated}, Created={created}";
            return RedirectToAction(nameof(Spreadsheet));
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result;
            var cur = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cur.Append('"'); i++; // escaped quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(cur.ToString()); cur.Clear();
                }
                else
                {
                    cur.Append(c);
                }
            }
            result.Add(cur.ToString());
            return result;
        }

        // POST: Asset/GenerateLabel
        [HttpPost]
        public async Task<IActionResult> GenerateLabel(int? assetId)
        {
            // simple unique inventory number generator: prefix + timestamp + random
            var inv = $"INV-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString().Substring(0,6).ToUpperInvariant()}";

            // If assetId provided, create a Label record and link it to the asset (don't overwrite SKU)
            if (assetId.HasValue)
            {
                var asset = await _context.Assets.FindAsync(assetId.Value);
                if (asset != null)
                {
                    var label = new MediaTracker.Models.Label
                    {
                        AssetId = asset.Id,
                        InventoryNumber = inv,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Add(label);
                    await _context.SaveChangesAsync();
                }
            }

            // generate PDF417 barcode image bytes
            using var bitmap = GeneratePdf417(inv);
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            return File(ms.ToArray(), "image/png", "label.png");
        }

        // POST: Asset/AssignLabel (assign a label to a specific unlabeled unit)
        [HttpPost]
        public async Task<IActionResult> AssignLabel(int assetId)
        {
            var asset = await _context.Assets.FindAsync(assetId);
            if (asset == null) return NotFound();

            var inv = $"INV-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString().Substring(0,6).ToUpperInvariant()}";
            var label = new MediaTracker.Models.Label
            {
                AssetId = asset.Id,
                InventoryNumber = inv,
                CreatedAt = DateTime.UtcNow
            };
            _context.Add(label);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // POST: Asset/AssignLabelToUnit - create a label with a specific inventory number provided by user
        [HttpPost]
        public async Task<IActionResult> AssignLabelToUnit(int assetId, string inventoryNumber)
        {
            if (string.IsNullOrWhiteSpace(inventoryNumber))
            {
                TempData["Error"] = "Inventory number is required.";
                return RedirectToAction(nameof(Index));
            }

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset == null) return NotFound();

            // ensure uniqueness
            var exists = await _context.Labels.AnyAsync(l => l.InventoryNumber == inventoryNumber);
            if (exists)
            {
                TempData["Error"] = "Inventory number already exists.";
                return RedirectToAction(nameof(Index));
            }

            var label = new MediaTracker.Models.Label
            {
                AssetId = asset.Id,
                InventoryNumber = inventoryNumber,
                CreatedAt = DateTime.UtcNow
            };
            _context.Add(label);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: Asset/GetLabelImage?inventory=INV-...
        public IActionResult GetLabelImage(string inventory)
        {
            var bmp = GeneratePdf417(inventory);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return File(ms.ToArray(), "image/png");
        }

        // GET: Asset/UnitDetails/{labelId}
        public async Task<IActionResult> UnitDetails(int labelId)
        {
            var label = await _context.Labels.FindAsync(labelId);
            if (label == null) return NotFound();
            var asset = label.AssetId.HasValue ? await _context.Assets.FindAsync(label.AssetId.Value) : null;
            ViewBag.Asset = asset;
            return View(label);
        }

        // GET: Asset
        public async Task<IActionResult> Index(string searchString, AssetType? typeFilter)
        {
            searchString = searchString?.Trim();
            var assets = from a in _context.Assets select a;

            if (!string.IsNullOrEmpty(searchString))
            {
                // include assets where Name or SKU contain the search term or where a Label InventoryNumber matches
                var q = searchString;
                assets = assets.Where(s => s.Name.Contains(q)
                    || (!string.IsNullOrEmpty(s.SKU) && s.SKU.Contains(q))
                    || _context.Labels.Any(l => l.AssetId == s.Id && l.InventoryNumber.Contains(q)));
            }

            if (typeFilter.HasValue)
            {
                assets = assets.Where(x => x.Type == typeFilter.Value);
            }

            ViewData["CurrentSearch"] = searchString;
            ViewData["CurrentFilter"] = typeFilter;

            var model = await assets.AsNoTracking().ToListAsync();
            // build a flattened view: for each asset, show either unlabelled asset row(s) and each labeled item separately
            var allLabels = await _context.Labels.AsNoTracking().Where(l => l.AssetId != null).OrderBy(l => l.Id).ToListAsync();

            // map of assetId -> labels
            var labelGroups = allLabels.GroupBy(l => l.AssetId!.Value).ToDictionary(g => g.Key, g => g.ToList());

            ViewBag.AllLabels = allLabels;
            ViewBag.LabelGroups = labelGroups;
            ViewBag.ApprovedEmployees = await _context.Employees.AsNoTracking().Where(e => e.IsApproved).ToListAsync();

            return View(model);
        }

        // GET: Asset/Create
        public IActionResult Create() => View();

        // POST: Asset/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,Type,SKU,SerialNumber,ProductKey,Location,Quantity,Status")] Asset asset)
        {
            // Require at least one identifier: either SerialNumber or ProductKey
            var hasSerial = !string.IsNullOrWhiteSpace(asset.SerialNumber);
            var hasProduct = !string.IsNullOrWhiteSpace(asset.ProductKey);
            if (!hasSerial && !hasProduct)
            {
                ModelState.AddModelError(string.Empty, "Either a Serial Number or Product Key is required.");
            }

            if (ModelState.IsValid)
            {
                asset.LastUpdated = DateTime.UtcNow;
                _context.Add(asset);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(asset);
        }

        // GET: Asset/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var asset = await _context.Assets.FindAsync(id);
            if (asset == null) return NotFound();
            return View(asset);
        }

        // POST: Asset/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Type,SKU,SerialNumber,ProductKey,Location,Quantity,Status")] Asset asset)
        {
            if (id != asset.Id) return NotFound();

            // Require at least one identifier: either SerialNumber or ProductKey
            var hasSerialE = !string.IsNullOrWhiteSpace(asset.SerialNumber);
            var hasProductE = !string.IsNullOrWhiteSpace(asset.ProductKey);
            if (!hasSerialE && !hasProductE)
            {
                ModelState.AddModelError(string.Empty, "Either a Serial Number or Product Key is required.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    asset.LastUpdated = DateTime.UtcNow;
                    _context.Update(asset);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Assets.Any(e => e.Id == asset.Id)) return NotFound();
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(asset);
        }

        // POST: Asset/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var asset = await _context.Assets.FindAsync(id);
            if (asset != null)
            {
                _context.Assets.Remove(asset);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
