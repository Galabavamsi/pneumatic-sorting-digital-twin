namespace Kit1DigitalTwin
{
    public sealed class Kit1ComponentRegistry : KitComponentRegistry
    {
        protected override void Awake()
        {
            Configure("Kit 1");
            base.Awake();
        }
    }
}
