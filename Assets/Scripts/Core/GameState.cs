namespace LudoFriends.Core
{
    public class GameState
    {
        public int LastDiceValue { get; private set; } = -1;

        public int CurrentTurnPlayerIndex { get; set; } = 0;

        public void NextTurn(int playerCount)
        {
            CurrentTurnPlayerIndex = (CurrentTurnPlayerIndex + 1) % playerCount;
        }

        public void SetDiceValue(int value)
        {
            LastDiceValue = value;
        }

    }
}
