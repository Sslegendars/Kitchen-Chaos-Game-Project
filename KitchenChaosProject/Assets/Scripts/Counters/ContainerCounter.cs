using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

public class ContainerCounter : BaseCounter, IKitchenObjectParent
{
    public event EventHandler OnPlayerGrabbedObject;
    [SerializeField]
    private KitchenObjectSO kitchenObjectSO;
    
    public override void Interact(Player player)
    {   
        if(!player.HasKitchenObject())
        {   
            // Player is not carrying anything
            KitchenObject.SpawnKitchenObject(kitchenObjectSO,player);

            OnPlayerGrabbedObject?.Invoke(this, EventArgs.Empty);
        }
        
    }
   
}
