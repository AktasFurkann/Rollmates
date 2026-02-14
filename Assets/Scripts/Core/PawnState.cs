namespace LudoFriends.Core
{
    public enum PawnZone
    {
        Home,
        MainPath,
        HomeLane,
        Finished
    }

    public class PawnState
    {
        public PawnZone Zone { get; private set; } = PawnZone.Home;

public bool IsFinished { get; private set; }
public bool IsInHomeLane { get; private set; }
public int HomeIndex { get; private set; } = -1;

// Main path üstündeyken 0..51
public int MainIndex { get; private set; }

// Evde mi?
public bool IsAtHome => Zone == PawnZone.Home;

public void EnterHomeLane()
{
    IsInHomeLane = true;
    HomeIndex = 0;

    // ✅ kritik: artık main path'te değil
    MainIndex = -1;
}


public void AdvanceHome(int steps)
{
    HomeIndex += steps;
    if (HomeIndex >= 5)
    {
        HomeIndex = 5;
        IsFinished = true;
    }
}


        public void EnterMainAt(int startIndex)
        {
            Zone = PawnZone.MainPath;
            MainIndex = startIndex;
        }

        public void AdvanceMain(int steps, int mainCount)
        {
            MainIndex = (MainIndex + steps) % mainCount;
        }

        public void ReturnHome()
{
    Zone = PawnZone.Home;
    MainIndex = 0;

    IsFinished = false;
    IsInHomeLane = false;
    HomeIndex = -1;
}



    }
}
