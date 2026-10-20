namespace OfficeCopilot.Host.Excel.Analysis.Accumulators
{
    internal sealed class GroupAccumulator
    {
        public GroupAccumulator(string[] keyParts, int measureCount)
        {
            KeyParts = keyParts;
            Slots = new ScalarAccumulator[measureCount];
            for (var index = 0; index < measureCount; index++)
            {
                Slots[index] = new ScalarAccumulator();
            }
        }

        public string[] KeyParts { get; private set; }

        public ScalarAccumulator[] Slots { get; private set; }
    }
}
