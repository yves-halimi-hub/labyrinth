namespace EFYVLabyMake.Core.IO
{
    public static class DesignerPathPolicy
    {
        public static bool IsSafeFileStem(string value)
        {
            return EFYVBackend.Core.IO.SafePathPolicy.IsSafeFileStem(value);
        }

        public static string GetContainedPath(string rootDirectory, string fileName)
        {
            return EFYVBackend.Core.IO.SafePathPolicy.GetContainedPath(rootDirectory, fileName);
        }
    }
}
