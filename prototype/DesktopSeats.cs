using System.Windows;

namespace WhaleAlive;
public record DesktopSeat(string Id, string Name, Point Surface, int IconX, int IconY);
public interface IDesktopSeatSource
{
    IReadOnlyList<DesktopSeat> Available(PetWindow pet);
    bool StillThere(DesktopSeat seat);
}
public sealed class DesktopSeats : IDesktopSeatSource
{
    public IReadOnlyList<DesktopSeat> Available(PetWindow pet)
    {
        using var shell = new ShellDesktop();
        var seats = shell.VisibleSeats();
        return shell.List(false).Where(i => seats.ContainsKey(i.Name))
            .Select(i => new DesktopSeat(i.Id, i.Name, pet.FromPixels(seats[i.Name]), i.X, i.Y))
            .Where(v => Fits(DisplayGeometry.WorkArea(pet, v.Surface), v.Surface)).ToArray();
    }
    static bool Fits(Rect work, Point p) => p.Y > work.Top + 210 && p.Y < work.Bottom - 65 && p.X > work.Left + 100 && p.X < work.Right - 110;
    public bool StillThere(DesktopSeat seat)
    {
        using var shell = new ShellDesktop();
        return shell.List(false).Any(i => i.Id == seat.Id && i.X == seat.IconX && i.Y == seat.IconY);
    }
}
