using TaleWorlds.Library;

namespace Diplomacy.Companion
{
    public class CompanionHighlighterVM : ViewModel
    {
        private CompanionIconVM _companion1;
        private CompanionIconVM _companion2;
        private CompanionIconVM _companion3;
        private CompanionIconVM _companion4;
        private CompanionIconVM _companion5;

        public CompanionHighlighterVM()
        {
            // Initialize with empty companion VMs
            Companion1 = new CompanionIconVM();
            Companion2 = new CompanionIconVM();
            Companion3 = new CompanionIconVM();
            Companion4 = new CompanionIconVM();
            Companion5 = new CompanionIconVM();
        }

        [DataSourceProperty]
        public CompanionIconVM Companion1
        {
            get => _companion1;
            set
            {
                if (value != _companion1)
                {
                    _companion1 = value;
                    OnPropertyChangedWithValue(value, "Companion1");
                }
            }
        }

        [DataSourceProperty]
        public CompanionIconVM Companion2
        {
            get => _companion2;
            set
            {
                if (value != _companion2)
                {
                    _companion2 = value;
                    OnPropertyChangedWithValue(value, "Companion2");
                }
            }
        }

        [DataSourceProperty]
        public CompanionIconVM Companion3
        {
            get => _companion3;
            set
            {
                if (value != _companion3)
                {
                    _companion3 = value;
                    OnPropertyChangedWithValue(value, "Companion3");
                }
            }
        }

        [DataSourceProperty]
        public CompanionIconVM Companion4
        {
            get => _companion4;
            set
            {
                if (value != _companion4)
                {
                    _companion4 = value;
                    OnPropertyChangedWithValue(value, "Companion4");
                }
            }
        }

        [DataSourceProperty]
        public CompanionIconVM Companion5
        {
            get => _companion5;
            set
            {
                if (value != _companion5)
                {
                    _companion5 = value;
                    OnPropertyChangedWithValue(value, "Companion5");
                }
            }
        }

        public CompanionIconVM GetCompanionSlot(int index)
        {
            switch (index)
            {
                case 0: return Companion1;
                case 1: return Companion2;
                case 2: return Companion3;
                case 3: return Companion4;
                case 4: return Companion5;
                default: return null;
            }
        }
    }
}