namespace StockPilot.Web.Infrastructure;

/// <summary>Expected business-rule failure. Safe to show to the user.</summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message)
    {
    }
}

public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message)
    {
    }
}
