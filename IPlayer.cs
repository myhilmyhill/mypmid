public interface IPlayer
{
    /// <summary>
    /// if true continues, if false ended
    /// </summary>
    bool Play();
    void Finally();
    void SetMap(MidiMap map);
}
