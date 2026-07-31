using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class SupplierService : ISupplierService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantCacheService? _cache;

    public SupplierService(AppDbContext dbContext, ITenantCacheService? cache = null)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<Supplier?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Supplier>()
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<Supplier>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.Suppliers,
                $"p{page}:s{pageSize}",
                () => LoadSuppliersAsync(search, page, pageSize, ct),
                ct: ct);
        }

        return await LoadSuppliersAsync(search, page, pageSize, ct);
    }

    private async Task<IReadOnlyList<Supplier>> LoadSuppliersAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = _dbContext.Set<Supplier>()
            .AsNoTracking()
            .Where(s => s.IsActive)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(s =>
                s.Name.ToLower().Contains(term) ||
                (s.ContactPerson != null && s.ContactPerson.ToLower().Contains(term)) ||
                (s.Email != null && s.Email.ToLower().Contains(term)));
        }

        return await query
            .OrderBy(s => s.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(Supplier supplier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(supplier.Name))
            throw new InvalidOperationException("Supplier name is required.");

        supplier.Name = supplier.Name.Trim();
        if (supplier.Name.Length > 200)
            throw new InvalidOperationException("Supplier name cannot exceed 200 characters.");
        if (!string.IsNullOrWhiteSpace(supplier.Email))
        {
            supplier.Email = supplier.Email.Trim();
            if (!IsPlausibleEmail(supplier.Email))
                throw new InvalidOperationException("Supplier email must be a valid address.");
        }
        if (!string.IsNullOrWhiteSpace(supplier.Phone))
        {
            supplier.Phone = supplier.Phone.Trim();
            if (supplier.Phone.Length > 50)
                throw new InvalidOperationException("Supplier phone cannot exceed 50 characters.");
        }
        if (!string.IsNullOrWhiteSpace(supplier.Notes))
        {
            supplier.Notes = supplier.Notes.Trim();
            if (supplier.Notes.Length > 2000)
                throw new InvalidOperationException("Supplier notes cannot exceed 2000 characters.");
        }
        NormalizeSupplierAddress(supplier);

        var nameTaken = await _dbContext.Set<Supplier>()
            .AnyAsync(s => s.Name == supplier.Name && s.IsActive, ct);
        if (nameTaken)
            throw new InvalidOperationException($"Supplier '{supplier.Name}' already exists.");

        if (!string.IsNullOrWhiteSpace(supplier.Email))
        {
            var emailTaken = await _dbContext.Set<Supplier>()
                .AnyAsync(s => s.Email == supplier.Email && s.IsActive, ct);
            if (emailTaken)
                throw new InvalidOperationException($"Supplier email '{supplier.Email}' is already in use.");
        }

        _dbContext.Set<Supplier>().Add(supplier);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
        return supplier.Id;
    }

    public async Task UpdateAsync(Supplier supplier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(supplier.Name))
            throw new InvalidOperationException("Supplier name is required.");

        supplier.Name = supplier.Name.Trim();
        if (supplier.Name.Length > 200)
            throw new InvalidOperationException("Supplier name cannot exceed 200 characters.");
        if (!string.IsNullOrWhiteSpace(supplier.Email))
        {
            supplier.Email = supplier.Email.Trim();
            if (!IsPlausibleEmail(supplier.Email))
                throw new InvalidOperationException("Supplier email must be a valid address.");
        }
        if (!string.IsNullOrWhiteSpace(supplier.Phone))
        {
            supplier.Phone = supplier.Phone.Trim();
            if (supplier.Phone.Length > 50)
                throw new InvalidOperationException("Supplier phone cannot exceed 50 characters.");
        }
        if (!string.IsNullOrWhiteSpace(supplier.Notes))
        {
            supplier.Notes = supplier.Notes.Trim();
            if (supplier.Notes.Length > 2000)
                throw new InvalidOperationException("Supplier notes cannot exceed 2000 characters.");
        }
        NormalizeSupplierAddress(supplier);

        var nameTaken = await _dbContext.Set<Supplier>()
            .AnyAsync(s => s.Name == supplier.Name && s.Id != supplier.Id && s.IsActive, ct);
        if (nameTaken)
            throw new InvalidOperationException($"Supplier '{supplier.Name}' already exists.");

        if (!string.IsNullOrWhiteSpace(supplier.Email))
        {
            var emailTaken = await _dbContext.Set<Supplier>()
                .AnyAsync(s => s.Email == supplier.Email && s.Id != supplier.Id && s.IsActive, ct);
            if (emailTaken)
                throw new InvalidOperationException($"Supplier email '{supplier.Email}' is already in use.");
        }

        _dbContext.Set<Supplier>().Update(supplier);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var supplier = await _dbContext.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier == null) return;

        var hasOpenPos = await _dbContext.Set<PurchaseOrder>().AsNoTracking()
            .AnyAsync(p => p.SupplierId == id
                && p.Status != PurchaseOrderStatus.Cancelled
                && p.Status != PurchaseOrderStatus.Received, ct);
        if (hasOpenPos)
            throw new InvalidOperationException(
                "Cannot delete a supplier with open purchase orders. Cancel or receive them first.");

        // Soft-delete and deactivate so pickers hide the supplier.
        supplier.IsActive = false;
        supplier.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    private void InvalidateListCaches()
    {
        if (_cache != null)
            TenantCacheInvalidation.OnSupplierMasterDataChanged(_cache);
    }

    private static void NormalizeSupplierAddress(Supplier supplier)
    {
        supplier.AddressLine1 = BoundOptional(supplier.AddressLine1, 200, "Address line 1");
        supplier.AddressLine2 = BoundOptional(supplier.AddressLine2, 200, "Address line 2");
        supplier.City = BoundOptional(supplier.City, 100, "City");
        supplier.Province = BoundOptional(supplier.Province, 100, "Province");
        supplier.PostalCode = BoundOptional(supplier.PostalCode, 20, "Postal code");
        supplier.Country = BoundOptional(supplier.Country, 100, "Country");
        supplier.ContactPerson = BoundOptional(supplier.ContactPerson, 200, "Contact person");
        supplier.TaxNumber = BoundOptional(supplier.TaxNumber, 50, "Tax number");
    }

    private static string? BoundOptional(string? value, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (value.Length > maxLength)
            throw new InvalidOperationException($"{fieldName} cannot exceed {maxLength} characters.");
        return value;
    }

    private static bool IsPlausibleEmail(string email) =>
        email.Contains('@') && !email.StartsWith('@') && !email.EndsWith('@');
}