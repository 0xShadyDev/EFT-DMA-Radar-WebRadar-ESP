using MaterialSkin;
using MaterialSkin.Controls;
using static System.Net.Mime.MediaTypeNames;

public class MonitorListBoxItem
{
    public string Name { get; }
    public string Resolution { get; }

    public MonitorListBoxItem(string name, string resolution)
    {
        Name = name;
        Resolution = resolution;
    }

    public override string ToString()
    {
        return $"{Name} ({Resolution})";
    }
}
