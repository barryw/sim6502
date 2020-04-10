namespace sim6502.Proc
{
    /// <summary>
    /// Used by the testing framework to keep track of the resources that we have to load
    /// on each test executed. The resources are specified at the suite level, but since
    /// the processor is reset on each test, we need to reload these resources so that our
    /// tests can run cleanly.
    /// </summary>
    public class LoadableResource
    {
        /// <summary>
        /// The filename of the resource to load
        /// </summary>
        public string Filename { get; set; }
        
        /// <summary>
        /// The address to load the resource at. If not specified, will use the first 2 bytes
        /// as the load address. If the load address is the first 2 bytes, make sure you specify
        /// StripHeader = true so that we don't load the header bytes into memory.
        /// </summary>
        public int LoadAddress { get; set; }

        /// <summary>
        /// Whether to strip the first 2 bytes of the file. For .prg files, you generally want to set this
        /// to true since the load address was specified using the first 2 bytes of the file. For ROMS like
        /// KERNAL and BASIC, leave this as false
        /// </summary>
        public bool StripHeader { get; set; } = false;
    }
}