using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltraLibrarianImporter.KiCadBindings
{
    public interface IKiCadFactory
    {
        public KiCad Create(string? clientName = null);
    }
}
