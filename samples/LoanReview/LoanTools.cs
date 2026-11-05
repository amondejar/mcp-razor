using System.ComponentModel;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;

namespace LoanReview;

[McpServerToolType]
public sealed class LoanTools(LoanService loans)
{
    [McpServerTool(Name = "review_loans", ReadOnly = true, UseStructuredContent = true)]
    [McpAppUi(ResourceUri = "ui://loans/review")]
    [Description("Shows the pending loan applications in an interactive panel where the user can approve or reject each one.")]
    public LoanQueue ReviewLoans(
        [Description("Only show applications of at least this amount, in euros.")] decimal minAmount = 0) =>
        new(loans.Pending(minAmount));

    [McpServerTool(Name = "new_customer", UseStructuredContent = true)]
    [McpAppUi(ResourceUri = "ui://customers/new")]
    [Description("Opens a form to register a new customer. Pass what you already know; the user completes and submits the form.")]
    public CustomerDraft NewCustomer(
        [Description("Customer full name, if known.")] string? name = null,
        [Description("Customer email, if known.")] string? email = null) =>
        new() { Name = name, Email = email };
}
