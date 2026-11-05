using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;

namespace LoanReview;

public enum LoanStatus { Pending, Approved, Rejected }

public sealed record LoanApplication(
    string Id,
    string Applicant,
    decimal Amount,
    int TermMonths,
    string Purpose,
    int RiskScore,
    LoanStatus Status = LoanStatus.Pending,
    string? Note = null);

/// <summary>What the review_loans tool returns as structured content.</summary>
public sealed record LoanQueue(IReadOnlyList<LoanApplication> Loans, string Currency = "EUR");

/// <summary>What the new_customer tool returns: a draft the user completes in the form.</summary>
public sealed class CustomerDraft
{
    [Required(ErrorMessage = "The name is required.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "The name must be between 2 and 80 characters.")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "The email is required.")]
    [EmailAddress(ErrorMessage = "This is not a valid email address.")]
    public string? Email { get; set; }

    [Range(typeof(decimal), "0", "1000000", ErrorMessage = "The monthly income must be between 0 and 1,000,000.")]
    public decimal MonthlyIncome { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "The customer must accept the privacy policy.")]
    public bool AcceptsPrivacyPolicy { get; set; }
}

/// <summary>In-memory loan book for the sample.</summary>
public sealed class LoanService
{
    private readonly ConcurrentDictionary<string, LoanApplication> _loans = new(new Dictionary<string, LoanApplication>
    {
        ["L-1042"] = new("L-1042", "María López", 12_000m, 36, "Car", 712),
        ["L-1043"] = new("L-1043", "Jordi Puig", 45_000m, 84, "Home renovation", 655),
        ["L-1044"] = new("L-1044", "Lucía Fernández", 3_500m, 12, "Studies", 780),
        ["L-1045"] = new("L-1045", "Andrés Ruiz", 28_000m, 60, "Business equipment", 590),
    });

    private readonly ConcurrentDictionary<string, CustomerDraft> _customers = new();

    public IReadOnlyList<LoanApplication> All() => _loans.Values.OrderBy(loan => loan.Id, StringComparer.Ordinal).ToList();

    public IReadOnlyList<LoanApplication> Pending(decimal minAmount = 0) =>
        All().Where(loan => loan.Status == LoanStatus.Pending && loan.Amount >= minAmount).ToList();

    public LoanApplication Approve(string id, string? note) => Decide(id, LoanStatus.Approved, note);

    public LoanApplication Reject(string id, string? note) => Decide(id, LoanStatus.Rejected, note);

    public string AddCustomer(CustomerDraft customer)
    {
        var id = $"C-{_customers.Count + 501}";
        _customers[id] = customer;
        return id;
    }

    private LoanApplication Decide(string id, LoanStatus status, string? note)
    {
        var loan = _loans.TryGetValue(id, out var current) ? current : throw new KeyNotFoundException($"Loan {id} not found.");
        if (loan.Status != LoanStatus.Pending)
        {
            throw new InvalidOperationException($"Loan {id} was already {loan.Status.ToString().ToLowerInvariant()}.");
        }

        var decided = loan with { Status = status, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() };
        _loans[id] = decided;
        return decided;
    }
}
