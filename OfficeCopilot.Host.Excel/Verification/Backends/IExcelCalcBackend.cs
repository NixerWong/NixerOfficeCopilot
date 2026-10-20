using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Verification.Backends
{
    public interface IExcelCalcBackend
    {
        string Name { get; }

        bool CanEvaluate(CalcQuery query, CalcCostEstimate cost);

        CalcResult Evaluate(CalcQuery query);
    }
}
