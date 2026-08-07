using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class CustomerService : ICustomerService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantCacheService? _cache;

    public CustomerService(AppDbContext dbContext, ITenantCacheService? cache = null)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Customer>()
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IReadOnlyList<Customer>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.Customers,
                $"p{page}:s{pageSize}",
                () => LoadCustomersAsync(search, page, pageSize, ct),
                ct: ct);
        }

        return await LoadCustomersAsync(search, page, pageSize, ct);
    }

    private async Task<IReadOnlyList<Customer>> LoadCustomersAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = _dbContext.Set<Customer>().AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term) ||
                (c.Email != null && c.Email.ToLower().Contains(term)) ||
                (c.Phone != null && c.Phone.Contains(term)));
        }

        return await query
            .Include(c => c.Contacts)
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(Customer customer, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(customer.Name))
            throw new InvalidOperationException("Customer name is required.");

        customer.Name = customer.Name.Trim();
        if (customer.Name.Length > 200)
            throw new InvalidOperationException("Customer name cannot exceed 200 characters.");
        if (!string.IsNullOrWhiteSpace(customer.Email))
        {
            customer.Email = customer.Email.Trim();
            if (!IsPlausibleEmail(customer.Email))
                throw new InvalidOperationException("Customer email must be a valid address.");
            if (customer.Email.Length > 200)
                throw new InvalidOperationException("Customer email cannot exceed 200 characters.");
        }
        if (!string.IsNullOrWhiteSpace(customer.Phone))
        {
            customer.Phone = customer.Phone.Trim();
            if (customer.Phone.Length > 50)
                throw new InvalidOperationException("Customer phone cannot exceed 50 characters.");
        }
        if (!string.IsNullOrWhiteSpace(customer.Notes))
        {
            customer.Notes = customer.Notes.Trim();
            if (customer.Notes.Length > 2000)
                throw new InvalidOperationException("Customer notes cannot exceed 2000 characters.");
        }
        NormalizeCustomerAddress(customer);

        var nameTaken = await _dbContext.Set<Customer>()
            .AnyAsync(c => c.Name == customer.Name, ct);
        if (nameTaken)
            throw new InvalidOperationException($"Customer '{customer.Name}' already exists.");

        if (!string.IsNullOrWhiteSpace(customer.Email))
        {
            var emailTaken = await _dbContext.Set<Customer>()
                .AnyAsync(c => c.Email == customer.Email, ct);
            if (emailTaken)
                throw new InvalidOperationException($"Customer email '{customer.Email}' is already in use.");
        }

        _dbContext.Set<Customer>().Add(customer);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
        return customer.Id;
    }

    public async Task UpdateAsync(Customer customer, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(customer.Name))
            throw new InvalidOperationException("Customer name is required.");

        customer.Name = customer.Name.Trim();
        if (customer.Name.Length > 200)
            throw new InvalidOperationException("Customer name cannot exceed 200 characters.");
        if (!string.IsNullOrWhiteSpace(customer.Email))
        {
            customer.Email = customer.Email.Trim();
            if (!IsPlausibleEmail(customer.Email))
                throw new InvalidOperationException("Customer email must be a valid address.");
            if (customer.Email.Length > 200)
                throw new InvalidOperationException("Customer email cannot exceed 200 characters.");
        }
        if (!string.IsNullOrWhiteSpace(customer.Phone))
        {
            customer.Phone = customer.Phone.Trim();
            if (customer.Phone.Length > 50)
                throw new InvalidOperationException("Customer phone cannot exceed 50 characters.");
        }
        if (!string.IsNullOrWhiteSpace(customer.Notes))
        {
            customer.Notes = customer.Notes.Trim();
            if (customer.Notes.Length > 2000)
                throw new InvalidOperationException("Customer notes cannot exceed 2000 characters.");
        }
        NormalizeCustomerAddress(customer);

        var nameTaken = await _dbContext.Set<Customer>()
            .AnyAsync(c => c.Name == customer.Name && c.Id != customer.Id, ct);
        if (nameTaken)
            throw new InvalidOperationException($"Customer '{customer.Name}' already exists.");

        if (!string.IsNullOrWhiteSpace(customer.Email))
        {
            var emailTaken = await _dbContext.Set<Customer>()
                .AnyAsync(c => c.Email == customer.Email && c.Id != customer.Id, ct);
            if (emailTaken)
                throw new InvalidOperationException($"Customer email '{customer.Email}' is already in use.");
        }

        _dbContext.Set<Customer>().Update(customer);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await _dbContext.Set<Customer>()
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (customer == null) return;

        var hasOpenJobs = await _dbContext.Set<Job>().AsNoTracking()
            .AnyAsync(j => j.CustomerId == id
                && j.Status != JobStatus.Cancelled
                && j.Status != JobStatus.Closed, ct);
        if (hasOpenJobs)
            throw new InvalidOperationException(
                "Cannot delete a customer with open jobs. Close or cancel them first.");

        var hasOpenQuotes = await _dbContext.Set<Quote>().AsNoTracking()
            .AnyAsync(q => q.CustomerId == id
                && q.Status != QuoteStatus.Rejected
                && q.Status != QuoteStatus.Expired, ct);
        if (hasOpenQuotes)
            throw new InvalidOperationException(
                "Cannot delete a customer with open quotes. Reject or expire them first.");

        var hasOpenSalesOrders = await _dbContext.Set<SalesOrder>().AsNoTracking()
            .AnyAsync(s => s.CustomerId == id
                && s.Status != SalesOrderStatus.Cancelled
                && s.Status != SalesOrderStatus.Completed, ct);
        if (hasOpenSalesOrders)
            throw new InvalidOperationException(
                "Cannot delete a customer with open sales orders. Complete or cancel them first.");

        var hasUnpaidInvoices = await _dbContext.Set<Invoice>().AsNoTracking()
            .AnyAsync(i => i.CustomerId == id
                && i.Status != InvoiceStatus.Cancelled
                && i.Status != InvoiceStatus.Paid
                && i.DocumentType != InvoiceDocumentType.CreditNote
                && i.DocumentType != InvoiceDocumentType.Proforma, ct);
        if (hasUnpaidInvoices)
            throw new InvalidOperationException(
                "Cannot delete a customer with open or unpaid invoices. Settle or cancel them first.");

        foreach (var contact in customer.Contacts)
        {
            contact.IsDeleted = true;
        }
        customer.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task<IReadOnlyList<Contact>> GetContactsAsync(Guid customerId, CancellationToken ct = default)
    {
        return await _dbContext.Set<Contact>()
            .Where(c => c.CustomerId == customerId)
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.LastName)
            .ToListAsync(ct);
    }

    public async Task<Guid> AddContactAsync(Contact contact, CancellationToken ct = default)
    {
        ValidateContact(contact);

        // Soft-delete aware so deleted customers are not a vague "not found".
        var customer = await _dbContext.Set<Customer>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contact.CustomerId, ct);
        if (customer == null || customer.IsDeleted)
            throw new InvalidOperationException("Customer not found or deleted.");

        if (contact.IsPrimary)
        {
            var existingPrimaries = await _dbContext.Set<Contact>()
                .Where(c => c.CustomerId == contact.CustomerId && c.IsPrimary)
                .ToListAsync(ct);

            foreach (var p in existingPrimaries)
            {
                p.IsPrimary = false;
            }
        }

        _dbContext.Set<Contact>().Add(contact);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
        return contact.Id;
    }

    public async Task UpdateContactAsync(Contact contact, CancellationToken ct = default)
    {
        ValidateContact(contact);

        if (contact.IsPrimary)
        {
            var existingPrimaries = await _dbContext.Set<Contact>()
                .Where(c => c.CustomerId == contact.CustomerId && c.IsPrimary && c.Id != contact.Id)
                .ToListAsync(ct);

            foreach (var p in existingPrimaries)
            {
                p.IsPrimary = false;
            }
        }

        _dbContext.Set<Contact>().Update(contact);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    private static void ValidateContact(Contact contact)
    {
        if (contact.CustomerId == Guid.Empty)
            throw new InvalidOperationException("Customer is required for a contact.");
        if (string.IsNullOrWhiteSpace(contact.FirstName) && string.IsNullOrWhiteSpace(contact.LastName))
            throw new InvalidOperationException("Contact first or last name is required.");

        contact.FirstName = (contact.FirstName ?? string.Empty).Trim();
        contact.LastName = (contact.LastName ?? string.Empty).Trim();
        if (contact.FirstName.Length > 100 || contact.LastName.Length > 100)
            throw new InvalidOperationException("Contact first and last names cannot exceed 100 characters each.");
        if (!string.IsNullOrWhiteSpace(contact.Email))
        {
            contact.Email = contact.Email.Trim();
            if (!IsPlausibleEmail(contact.Email))
                throw new InvalidOperationException("Contact email must be a valid address.");
            if (contact.Email.Length > 200)
                throw new InvalidOperationException("Contact email cannot exceed 200 characters.");
        }
        if (!string.IsNullOrWhiteSpace(contact.Phone))
        {
            contact.Phone = contact.Phone.Trim();
            if (contact.Phone.Length > 50)
                throw new InvalidOperationException("Contact phone cannot exceed 50 characters.");
        }
        if (!string.IsNullOrWhiteSpace(contact.Notes))
        {
            contact.Notes = contact.Notes.Trim();
            if (contact.Notes.Length > 500)
                throw new InvalidOperationException("Contact notes cannot exceed 500 characters.");
        }
    }

    private static void NormalizeCustomerAddress(Customer customer)
    {
        customer.AddressLine1 = BoundOptional(customer.AddressLine1, 200, "Address line 1");
        customer.AddressLine2 = BoundOptional(customer.AddressLine2, 200, "Address line 2");
        customer.City = BoundOptional(customer.City, 100, "City");
        customer.Province = BoundOptional(customer.Province, 100, "Province");
        customer.PostalCode = BoundOptional(customer.PostalCode, 20, "Postal code");
        customer.Country = BoundOptional(customer.Country, 100, "Country");
        customer.CompanyRegistrationNumber = BoundOptional(customer.CompanyRegistrationNumber, 50, "Company registration number");
        customer.VatNumber = BoundOptional(customer.VatNumber, 50, "VAT number");
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

    public async Task DeleteContactAsync(Guid contactId, CancellationToken ct = default)
    {
        var contact = await _dbContext.Set<Contact>().FirstOrDefaultAsync(c => c.Id == contactId, ct);
        if (contact == null) return;

        if (contact.IsPrimary)
        {
            var otherContacts = await _dbContext.Set<Contact>()
                .Where(c => c.CustomerId == contact.CustomerId && c.Id != contact.Id)
                .ToListAsync(ct);
            if (otherContacts.Count > 0)
            {
                // Promote another contact so the customer always has a primary when contacts remain.
                var next = otherContacts
                    .OrderByDescending(c => c.LastName)
                    .ThenBy(c => c.FirstName)
                    .First();
                next.IsPrimary = true;
            }
        }

        contact.IsPrimary = false;
        contact.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    private void InvalidateListCaches()
    {
        if (_cache != null)
            TenantCacheInvalidation.OnCustomerMasterDataChanged(_cache);
    }
}