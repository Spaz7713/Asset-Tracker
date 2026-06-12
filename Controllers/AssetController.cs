// Controllers/AssetController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MediaTracker.Data;
using MediaTracker.Models;

namespace MediaTracker.Controllers
{
    public class AssetController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AssetController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Asset
        public async Task<IActionResult> Index(string searchString, AssetType? typeFilter)
        {
            searchString = searchString?.Trim();
            var assets = from a in _context.Assets select a;

            if (!string.IsNullOrEmpty(searchString))
            {
                assets = assets.Where(s => s.Name.Contains(searchString)
                    || (!string.IsNullOrEmpty(s.SKU) && s.SKU.Contains(searchString)));
            }

            if (typeFilter.HasValue)
            {
                assets = assets.Where(x => x.Type == typeFilter.Value);
            }

            ViewData["CurrentSearch"] = searchString;
            ViewData["CurrentFilter"] = typeFilter;

            return View(await assets.AsNoTracking().ToListAsync());
        }

        // GET: Asset/Create
        public IActionResult Create() => View();

        // POST: Asset/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,Type,SKU,Location,Quantity,Status")] Asset asset)
        {
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
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Type,SKU,Location,Quantity,Status")] Asset asset)
        {
            if (id != asset.Id) return NotFound();

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
