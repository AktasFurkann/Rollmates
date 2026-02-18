namespace LudoFriends.Core
{
    public class GameState
    {
        public int LastDiceValue { get; private set; } = -1;

        public int CurrentTurnPlayerIndex { get; set; } = 0;

        public void NextTurn(int playerCount)
        {
            if (playerCount <= 2)
            {
                CurrentTurnPlayerIndex = (CurrentTurnPlayerIndex + 1) % 2;
                return;
            }

            // Saat yönü sırası: Red(0) → Green(2) → Yellow(1) → Blue(3) → Red(0)
            switch (CurrentTurnPlayerIndex)
            {
                case 0: CurrentTurnPlayerIndex = 2; break;
                case 2: CurrentTurnPlayerIndex = 1; break;
                case 1: CurrentTurnPlayerIndex = playerCount > 3 ? 3 : 0; break;
                case 3: CurrentTurnPlayerIndex = 0; break;
                default: CurrentTurnPlayerIndex = 0; break;
            }
        }

        public void SetDiceValue(int value)
        {
            LastDiceValue = value;
        }

    }
}
