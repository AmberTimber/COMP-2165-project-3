using UnityEditor;
using UnityEngine;

namespace COMP2165.Clicker.Editor
{
    // The workbook still points at individual PNGs; this source atlas is also sliced
    // in the Inspector so students can pick or replace tiles without editing code.
    public sealed class CookieAtlasImporter : AssetPostprocessor
    {
        const string AtlasPath = "Assets/Clicker/SpriteSheets/cookie_clicker_icons.png";
        const string ResourcesAtlasPath = "Assets/Resources/SpriteSheets/cookie_clicker_icons.png";
        const int TileSize = 48;
        const int Columns = 35;
        const int Rows = 44;

        void OnPreprocessTexture()
        {
            bool isAtlas = assetPath == AtlasPath || assetPath == ResourcesAtlasPath;
            if (!isAtlas && !assetPath.StartsWith("Assets/Clicker/UIPreview/") && !assetPath.StartsWith("Assets/Resources/UIPreview/")) return;

            var importer = (TextureImporter)assetImporter;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;

            if (!isAtlas) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = TileSize;
            importer.maxTextureSize = 4096;
            importer.npotScale = TextureImporterNPOTScale.None;

            var sprites = new SpriteMetaData[Columns * Rows];
            for (int row = 0; row < Rows; row++)
            for (int column = 0; column < Columns; column++)
            {
                int index = row * Columns + column;
                sprites[index] = new SpriteMetaData
                {
                    name = $"tile_{column:D2}_{row:D2}",
                    rect = new Rect(column * TileSize, (Rows - 1 - row) * TileSize, TileSize, TileSize),
                    alignment = (int)SpriteAlignment.Center
                };
            }

#pragma warning disable 0618
            importer.spritesheet = sprites;
#pragma warning restore 0618
        }
    }
}
