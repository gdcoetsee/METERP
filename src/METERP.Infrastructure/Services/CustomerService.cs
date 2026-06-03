using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class CustomerService : ICustomerService
{
    private readonly AppDbContext _dbContext;

    public CustomerService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Customer>()
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IReadOnlyList<Customer>> GetAllAsync(string? search = null, CancellationToken ct = default)
    {
        var query = _dbContext.Set<Customer>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term) ||
                (c.Email != null && c.Email.ToLower().Contains(term)) ||
                (c.Phone != null && c.Phone.Contains(term)));
        }

        return await query
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(Customer customer, CancellationToken ct = default)
    {
        _dbContext.Set<Customer>().Add(customer);
        await _dbContext.SaveChangesAsync(ct);
        return customer.Id;
    }

    public async Task UpdateAsync(Customer customer, CancellationToken ct = default)
    {
        _dbContext.Set<Customer>().Update(customer);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await _dbContext.Set<Customer>()
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (customer == null) return;

        // Soft delete contacts too
        foreach (var contact in customer.Contacts)
        {
            contact.IsDeleted = true;
        }
        customer.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);
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
        // Ensure only one primary if setting primary
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
        return contact.Id;
    }

    public async Task UpdateContactAsync(Contact contact, CancellationToken ct = default)
    {
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
    }

    public async Task DeleteContactAsync(Guid contactId, CancellationToken ct = default)
    {
        var contact = await _dbContext.Set<Contact>().FirstOrDefaultAsync(c => c.Id == contactId, ct);
        if (contact == null) return;

        contact.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
    }
}
