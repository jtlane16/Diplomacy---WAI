using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace CompanionHighlighter
{
    public class CompanionIconVM : ViewModel
    {
        private float _positionX;
        private float _positionY;
        private float _width = 30f;
        private float _height = 30f;
        private bool _isVisible;
        private string _companionName = "";
        private int _fontSize = 16;

        public CompanionIconVM()
        {
        }

        [DataSourceProperty]
        public float PositionX
        {
            get => _positionX;
            set
            {
                if (value != _positionX)
                {
                    _positionX = value;
                    OnPropertyChangedWithValue(value, "PositionX");
                }
            }
        }

        [DataSourceProperty]
        public float PositionY
        {
            get => _positionY;
            set
            {
                if (value != _positionY)
                {
                    _positionY = value;
                    OnPropertyChangedWithValue(value, "PositionY");
                }
            }
        }

        [DataSourceProperty]
        public float Width
        {
            get => _width;
            set
            {
                if (value != _width)
                {
                    _width = value;
                    OnPropertyChangedWithValue(value, "Width");
                }
            }
        }

        [DataSourceProperty]
        public float Height
        {
            get => _height;
            set
            {
                if (value != _height)
                {
                    _height = value;
                    OnPropertyChangedWithValue(value, "Height");
                }
            }
        }

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (value != _isVisible)
                {
                    _isVisible = value;
                    OnPropertyChangedWithValue(value, "IsVisible");
                }
            }
        }

        [DataSourceProperty]
        public string CompanionName
        {
            get => _companionName;
            set
            {
                if (value != _companionName)
                {
                    _companionName = value;
                    OnPropertyChangedWithValue(value, "CompanionName");
                }
            }
        }

        [DataSourceProperty]
        public int FontSize
        {
            get => _fontSize;
            set
            {
                if (value != _fontSize)
                {
                    _fontSize = value;
                    OnPropertyChangedWithValue(value, "FontSize");
                }
            }
        }
    }
}