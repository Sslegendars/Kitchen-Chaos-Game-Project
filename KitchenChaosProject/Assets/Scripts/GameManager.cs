using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class GameManager : NetworkBehaviour
{   

    public static GameManager Instance { get; private set; }

    public event EventHandler OnStateChanged;
    public event EventHandler OnLocalGamePaused;
    public event EventHandler OnLocalGameUnpaused;
    public event EventHandler OnMultiplayerGamePaused;
    public event EventHandler OnMultiplayerGameUnpaused;
    public event EventHandler OnlocalPlayerReadyChanged;
    private enum State
    {
        WaitingToStart,
        CountdownToStart,
        GamePlaying,
        GameOver,
    }

    [SerializeField]
    private Transform playerPrefab;
    private NetworkVariable<State> state = new NetworkVariable<State>(State.WaitingToStart);
    private bool isLocalPlayerReady;
    private NetworkVariable<float> countdownToStartTimer = new NetworkVariable<float>(3f);
    private NetworkVariable<float> gamePlayingTimer = new NetworkVariable<float>(0f);
    private float gamePlayingTimerMax = 90f;
    private bool isLocalGamePaused = false;
    private NetworkVariable<bool> isGamePaused = new NetworkVariable<bool>(false);
    private Dictionary<ulong, bool> playerReadyDictionary;
    private Dictionary<ulong, bool> playerPauseDictionary;
    private bool autoTestGamePausedState;

   
    private const float addGamePlayingTimer = 10f;
    private const float subtractGamePlayingTimer = 5f;

    
    private void Awake()
    {
        Instance = this;
        
        playerReadyDictionary = new Dictionary<ulong, bool>();
        playerPauseDictionary = new Dictionary<ulong, bool>();
    }
    private void Start()
    {
        GameInput.Instance.OnPauseAction += GameInput_OnPauseAction;
        GameInput.Instance.OnInteractAction += GameInput_OnInteractAction;
        DeliveryManager.Instance.OnRecipeSuccess += DeliveryManager_OnRecipeSuccess;
        DeliveryManager.Instance.OnRecipeFailed += DeliveryManager_OnRecipeFailed;

        
    }
    
    public override void OnNetworkSpawn()
    {
        state.OnValueChanged += State_OnValueChanged;
        isGamePaused.OnValueChanged += IsGamePaused_OnValueChanged;

        if(IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += NetworkManager_OnClientDisconnectCallback;
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += SceneManager_OnLoadEventCompleted;
        }
    }

    private void SceneManager_OnLoadEventCompleted(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        foreach(ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            Transform playerTransform = Instantiate(playerPrefab);
            playerTransform.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
        }
    }

    private void NetworkManager_OnClientDisconnectCallback(ulong clientId)
    {   
        autoTestGamePausedState = true;
        TestGamePausedState();
    }

    private void IsGamePaused_OnValueChanged(bool previousValue, bool newValue)
    {
        if(isGamePaused.Value)
        {
            Time.timeScale = 0f;
            OnMultiplayerGamePaused?.Invoke(this,EventArgs.Empty);
        }
        else
        {
            Time.timeScale = 1f;
            OnMultiplayerGameUnpaused?.Invoke(this,EventArgs.Empty);
        }
    }

    private void State_OnValueChanged(State previousvalue, State newvalue)
    {
        OnStateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void DeliveryManager_OnRecipeFailed(object sender, EventArgs e)
    {
        SubtractGamePlayingTime();
    }

    private void DeliveryManager_OnRecipeSuccess(object sender, EventArgs e)
    {
        
        AddGamePlayingTime();
    }

    private void GameInput_OnInteractAction(object sender, EventArgs e)
    {
        if(state.Value == State.WaitingToStart)
        {
            isLocalPlayerReady = true;
            OnlocalPlayerReadyChanged?.Invoke(this, EventArgs.Empty);
            SetPlayerReadyServerRpc();            
        }
    }
    [ServerRpc(RequireOwnership = false)]
    private void SetPlayerReadyServerRpc(ServerRpcParams serverRpcParams = default)
    {
        playerReadyDictionary[serverRpcParams.Receive.SenderClientId] = true;

        bool allClientsReady = true;
        foreach(ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if(!playerReadyDictionary.ContainsKey(clientId) || !playerReadyDictionary[clientId])
            {
                // this player is NOT ready
                allClientsReady = false;
                break;
            }
        }

        if (allClientsReady)
        {
            state.Value = State.CountdownToStart;
        }        
    }
    private void GameInput_OnPauseAction(object sender, EventArgs e)
    {
        TogglePauseGame();
    }

    private void Update()
    {   
        if(!IsServer)
        {
            return;
        }

        switch (state.Value)
        {
            case State.WaitingToStart:               
                break;
            case State.CountdownToStart:
                countdownToStartTimer.Value -= Time.deltaTime;
                if (countdownToStartTimer.Value < 0f)
                {
                    state.Value = State.GamePlaying;
                    gamePlayingTimer.Value = gamePlayingTimerMax;                    
                }
                break;
            case State.GamePlaying:
                gamePlayingTimer.Value -= Time.deltaTime;
                if (gamePlayingTimer.Value < 0f)
                {
                    state.Value = State.GameOver;                   
                }
                break;
            case State.GameOver:
                break;           
        }
        
    }
    private void LateUpdate()
    {
        if (autoTestGamePausedState)
        {
            autoTestGamePausedState = false;
            TestGamePausedState();
        }
    }
    public bool IsGamePlaying()
    {
        return state.Value == State.GamePlaying;
    }
    public bool IsCountdownToStartActive()
    {
        return state.Value == State.CountdownToStart;
    }
    public float GetCountdownToStartTimer()
    {
        return countdownToStartTimer.Value;
    }
    public bool IsGameOver()
    {
        return state.Value == State.GameOver;
    }
    public bool IsWaitingToStart()
    {
        return state.Value == State.WaitingToStart;
    }
    public bool IsLocalPlayerReady()
    {
        return isLocalPlayerReady;
    }
    public float GetGamePlayingTimerNormalized()
    {
        return 1 - (gamePlayingTimer.Value / gamePlayingTimerMax);
    }
    public void TogglePauseGame()
    {
        isLocalGamePaused = !isLocalGamePaused;
        if (isLocalGamePaused)
        {   
            PauseGameServerRpc();
            
            OnLocalGamePaused?.Invoke(this,EventArgs.Empty);
        }
        else
        {   
            UnpauseGameServerRpc();
            
            OnLocalGameUnpaused?.Invoke(this,EventArgs.Empty);
        }
           
    }
    [ServerRpc(RequireOwnership = false)]
    private void PauseGameServerRpc(ServerRpcParams serverRpcParams = default)
    {
        playerPauseDictionary[serverRpcParams.Receive.SenderClientId] = true;
        TestGamePausedState();
    }
    [ServerRpc(RequireOwnership = false)]
    private void UnpauseGameServerRpc(ServerRpcParams serverRpcParams = default)
    {
        playerPauseDictionary[serverRpcParams.Receive.SenderClientId] = false;
        TestGamePausedState();
    }
    private void TestGamePausedState()
    {
        foreach(ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (playerPauseDictionary.ContainsKey(clientId) && playerPauseDictionary[clientId])
            {
                // this Player paused
                isGamePaused.Value = true;
                return;
            }
        }
        // All players unpaused
        isGamePaused.Value = false;
    }
    private void AddGamePlayingTime()
    {   
        if(gamePlayingTimer.Value < gamePlayingTimerMax)
        {   
            // if delivery recipe success
            gamePlayingTimer.Value += addGamePlayingTimer;if (gamePlayingTimer.Value > gamePlayingTimerMax)
            {
                gamePlayingTimer.Value = gamePlayingTimerMax;
            }
            
        }      
                
    }
    private void SubtractGamePlayingTime()
    {   
        if(gamePlayingTimer.Value > 0)
        {   
            // if delivery recipe failed
            gamePlayingTimer.Value -= subtractGamePlayingTimer;
            if(gamePlayingTimer.Value < 0)
            {
                gamePlayingTimer.Value = 0;
            }
        }
       
    }
    public float GetAddGamePlayingTimer()
    {
        return addGamePlayingTimer;
    }
    public float GetSubtractGamePlayingTimer()
    {
        return subtractGamePlayingTimer;
    }
}
