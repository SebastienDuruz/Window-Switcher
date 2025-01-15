namespace WindowSwitcherLib.WindowAccess;

public interface IDestroyableWindow
{
    public bool ToDestroy { get; set; }
    
    public void Destroy();
}