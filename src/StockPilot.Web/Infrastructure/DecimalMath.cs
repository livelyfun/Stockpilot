namespace StockPilot.Web.Infrastructure;

public static class DecimalMath
{
    public const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    public static decimal Money(decimal value) => Math.Round(value, 2, Rounding);

    public static decimal Quantity(decimal value) => Math.Round(value, 3, Rounding);

    public static decimal AverageCost(decimal value) => Math.Round(value, 4, Rounding);

    /// <summary>
    /// Weighted-average cost after adding stock at a known unit cost.
    /// </summary>
    public static decimal WeightedAverage(
        decimal currentQuantity,
        decimal currentAverageCost,
        decimal incomingQuantity,
        decimal incomingUnitCost)
    {
        var newQuantity = currentQuantity + incomingQuantity;
        if (newQuantity <= 0)
        {
            return 0m;
        }

        var totalValue = (currentQuantity * currentAverageCost) + (incomingQuantity * incomingUnitCost);
        return AverageCost(totalValue / newQuantity);
    }
}
