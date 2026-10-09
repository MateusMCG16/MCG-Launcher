using System.Diagnostics;

namespace MCG.Launcher.Core;

public static class GameLauncher
{
    public static ProcessStartInfo CreateStartInfo(GameEntry game)
    {
        if (game.LaunchKind == LaunchKind.Protocol)
        {
            if (!Uri.TryCreate(game.Target, UriKind.Absolute, out var uri))
                throw new InvalidOperationException("O endereço do jogo é inválido.");
            var valid = game.Source switch
            {
                GameSource.Steam => uri.Scheme == "steam" && uri.Host == "rungameid"
                    && ulong.TryParse(uri.AbsolutePath.Trim('/'), out _),
                GameSource.Epic => uri.Scheme == "com.epicgames.launcher" && uri.Host == "apps"
                    && !string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')),
                _ => false
            };
            if (!valid) throw new InvalidOperationException("Protocolo de inicialização não permitido.");
            return new ProcessStartInfo(game.Target) { UseShellExecute = true };
        }

        var extension = Path.GetExtension(game.Target);
        var allowed = game.LaunchKind switch
        {
            LaunchKind.Executable => extension.Equals(".exe", StringComparison.OrdinalIgnoreCase),
            LaunchKind.Shortcut => extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        if (!allowed || !Path.IsPathFullyQualified(game.Target) || !File.Exists(game.Target))
            throw new FileNotFoundException("O executável ou atalho não foi encontrado. Edite o cadastro para atualizar o caminho.");
        var workingDirectory = string.IsNullOrWhiteSpace(game.WorkingDirectory)
            ? game.LaunchKind == LaunchKind.Shortcut ? "" : Path.GetDirectoryName(game.Target)!
            : game.WorkingDirectory;
        if (workingDirectory.Length > 0 && !Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException("A pasta de trabalho do jogo não foi encontrada.");
        return new ProcessStartInfo(game.Target)
        {
            UseShellExecute = true,
            Arguments = game.Arguments,
            WorkingDirectory = workingDirectory
        };
    }

    public static void Launch(GameEntry game)
    {
        using var process = Process.Start(CreateStartInfo(game));
    }
}
