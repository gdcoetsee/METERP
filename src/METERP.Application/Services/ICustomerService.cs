using METERP.Domain;

namespace METERP.Application.Services;

public interface ICustomerService
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> GetAllAsync(string? search = null, CancellationToken ct = default);
    Task<Guid> CreateAsync(Customer customer, CancellationToken ct = default);
    Task UpdateAsync(Customer customer, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    // Contact management
    Task<IReadOnlyList<Contact>> GetContactsAsync(Guid customerId, CancellationToken ct = default);
    Task<Guid> AddContactAsync(Contact contact, CancellationToken ct = default);
    Task UpdateContactAsync(Contact contact, CancellationToken ct = default);
    Task DeleteContactAsync(Guid contactId, CancellationToken ct = default);
}
