using System.Windows.Media;
using FontAwesome.WPF;

namespace TinyCinema;

public static class IptvCategoryVisuals
{
    public static FontAwesomeIcon GetIcon(string slug) => slug switch
    {
        "__favorites__" => FontAwesomeIcon.Heart,
        "animation" => FontAwesomeIcon.Film,
        "auto" => FontAwesomeIcon.Car,
        "business" => FontAwesomeIcon.Briefcase,
        "classic" => FontAwesomeIcon.History,
        "comedy" => FontAwesomeIcon.ThumbsUp,
        "cooking" => FontAwesomeIcon.Cutlery,
        "culture" => FontAwesomeIcon.Institution,
        "documentary" => FontAwesomeIcon.Book,
        "education" => FontAwesomeIcon.GraduationCap,
        "entertainment" => FontAwesomeIcon.Star,
        "family" => FontAwesomeIcon.Users,
        "general" => FontAwesomeIcon.Television,
        "interactive" => FontAwesomeIcon.Comments,
        "kids" => FontAwesomeIcon.Child,
        "legislative" => FontAwesomeIcon.Legal,
        "lifestyle" => FontAwesomeIcon.Leaf,
        "movies" => FontAwesomeIcon.VideoCamera,
        "music" => FontAwesomeIcon.Music,
        "news" => FontAwesomeIcon.FileText,
        "outdoor" => FontAwesomeIcon.Tree,
        "public" => FontAwesomeIcon.Globe,
        "relax" => FontAwesomeIcon.Coffee,
        "religious" => FontAwesomeIcon.University,
        "science" => FontAwesomeIcon.Flask,
        "series" => FontAwesomeIcon.ListAlt,
        "shop" => FontAwesomeIcon.ShoppingCart,
        "sports" => FontAwesomeIcon.Trophy,
        "travel" => FontAwesomeIcon.Plane,
        "weather" => FontAwesomeIcon.Cloud,
        _ => FontAwesomeIcon.Television
    };

    public static Color GetAccentColor(string slug) => slug switch
    {
        "__favorites__" => Color.FromRgb(220, 38, 38),
        "news" => Color.FromRgb(59, 130, 246),
        "sports" => Color.FromRgb(34, 197, 94),
        "movies" => Color.FromRgb(168, 85, 247),
        "music" => Color.FromRgb(236, 72, 153),
        "kids" => Color.FromRgb(251, 191, 36),
        "documentary" => Color.FromRgb(20, 184, 166),
        "comedy" => Color.FromRgb(249, 115, 22),
        "entertainment" => Color.FromRgb(99, 102, 241),
        "education" => Color.FromRgb(14, 165, 233),
        "religious" => Color.FromRgb(139, 92, 246),
        "cooking" => Color.FromRgb(239, 68, 68),
        "travel" => Color.FromRgb(56, 189, 248),
        _ => Color.FromRgb(100, 116, 139)
    };

    public static Brush GetAccentBrush(string slug) =>
        new SolidColorBrush(GetAccentColor(slug));
}
