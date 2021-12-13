using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;



public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;

    LinkedList<PlayerAccount> playerAccounts;

    const int PlayerAccountNameAndPassword = 1;

    string playerAccountsFilePath;


    int playerWaitingForMatchWithID = -1;
    int spectatorJoiningMatchWithID = -1;

    LinkedList<GameRoom> gameRooms;



    // Start is called before the first frame update
    void Start()
    {
        Debug.Log("Server Running");
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        playerAccountsFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccounts.txt";
        playerAccounts = new LinkedList<PlayerAccount>();

        LoadPlayerAccounts();



        gameRooms = new LinkedList<GameRoom>();



    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }




    }

    public void SendMessageToClient(string msg, int id)
    {
        Debug.Log("Sending Message to client");
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);


    }

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] csv = msg.Split(',');
        Debug.Log(csv[0]);
        int signifier = int.Parse(csv[0]);

        if (signifier == ClientToServerSignifiers.CreateAccount)
        {

            Debug.Log("Create Account");

            string n = csv[1];
            string p = csv[2];
            bool nameIsInUse = false;

            foreach (PlayerAccount pa in playerAccounts)// check if name is in use
            {
                if (pa.name == n)
                {
                    nameIsInUse = true;
                    break;
                }
            }
            if (nameIsInUse)// account creation fail, name is in use
            {
                Debug.Log("Name is in use");
                SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + ",", id);

            }
            else// name is not in use, create account and save it to text file
            {
                Debug.Log("Name is not in use");
                PlayerAccount newPlayerAccount = new PlayerAccount(n, p);
                playerAccounts.AddLast(newPlayerAccount);
                SendMessageToClient(ServerToClientSignifiers.AccountCreationComplete + ",", id);

                SavePlayerAccounts();

            }
        }
        else if (signifier == ClientToServerSignifiers.Login)// login start
        {
            Debug.Log("Login start");

            string n = csv[1];
            string p = csv[2];
            bool hasNameBeenFound = false;
            bool msgHasBeenSentToClient = false;


            foreach (PlayerAccount pa in playerAccounts)// check for player account
            {
                if (pa.name == n)//name found!
                {

                    hasNameBeenFound = true;
                    Debug.Log("name founnd");

                    if (pa.password == p)// password found, login complete
                    {

                        SendMessageToClient(ServerToClientSignifiers.LoginComplete + ",", id);
                        msgHasBeenSentToClient = true;
                        Debug.Log("Username and Password found Login complete!");
                    }
                    else// username or password not found, login fail
                    {
                        Debug.Log("Username or Password NOT found Login FAIL!!!!!");
                        SendMessageToClient(ServerToClientSignifiers.LoginFailed + ",", id);
                        msgHasBeenSentToClient = true;


                    }
                    Debug.Log("Login Complete!!!!!!!!!!!");
                }
            }
            if (!hasNameBeenFound)// account not found, login fail
            {
                SendMessageToClient(ServerToClientSignifiers.LoginFailed + ",", id);
                Debug.Log("!hasNameBeenFound?");
                Debug.Log("Account Name " + n);

                if (!msgHasBeenSentToClient)// send error message
                {
                    Debug.Log("message not sent?");
                    SendMessageToClient(ServerToClientSignifiers.LoginFailed + ",", id);

                }
            }
        }
        else if (signifier == ClientToServerSignifiers.JoinQueueForGameRoom)//joining queue for game room
        {
            Debug.Log("Need to get player into a waiting queue!");


            if (playerWaitingForMatchWithID == -1)// check for player with ID
            {
                Debug.Log("Client is waiting for another player!");
                playerWaitingForMatchWithID = id;
            }
            else//if both clients are in queue create the game room
            {
                Debug.Log("Client in quese else");
                GameRoom gr = new GameRoom(playerWaitingForMatchWithID, id);
                gameRooms.AddLast(gr);

                SendMessageToClient(ServerToClientSignifiers.PlayerO + ",", gr.playerID2);
                SendMessageToClient(ServerToClientSignifiers.PlayerX + ",", gr.playerID1);
                SendMessageToClient(ServerToClientSignifiers.GameStart + ",", gr.playerID2);
                SendMessageToClient(ServerToClientSignifiers.GameStart + ",", gr.playerID1);
                //SendMessageToClient(ServerToClientSignifiers.SpectatorJoined + ",", gr.spectatorID);


                playerWaitingForMatchWithID = -1;
            }


        }
        else if (signifier == ClientToServerSignifiers.OpponentPlay)// start the game
        {
            Debug.Log("Opponents Turn!");
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {

                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + csv[1] + csv[2], gr.playerID2);

                }
                else
                {
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + csv[1] + csv[2], gr.playerID1);

                }

            }

        }
        else if (signifier == ClientToServerSignifiers.GameOver)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            string winnerID = csv[1];
            if (gr != null)
            {

                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.GameOver + "," + csv[1], gr.playerID2);

                }

                else
                {
                    SendMessageToClient(ServerToClientSignifiers.GameOver + "," + csv[1], gr.playerID1);

                }
            }
        }


    }


    private void SavePlayerAccounts()// save the players account on creation
    {
        Debug.Log("Start of SavePlayerAccounts");
        StreamWriter sw = new StreamWriter(playerAccountsFilePath);

        foreach (PlayerAccount pa in playerAccounts)
        {
            Debug.Log("ForEach SavePlayerAccounts");
            sw.WriteLine(PlayerAccountNameAndPassword + "," + pa.name + "," + pa.password);
        }
        sw.Close();
    }

    private void LoadPlayerAccounts()// load players account on login
    {
        Debug.Log("Start LoadPlayerAccounts");
        if (File.Exists(playerAccountsFilePath))
        {

            Debug.Log("Start File.Exists");

            StreamReader sr = new StreamReader(playerAccountsFilePath);

            string line;

            while (true)
            {
                Debug.Log("Start while(True)");
                line = sr.ReadLine();
                if (line == null)
                    break;

                string[] csv = line.Split(',');

                int signifier = int.Parse(csv[0]);

                if (signifier == PlayerAccountNameAndPassword)
                {
                    Debug.Log("Start signifier == PlayerAccountNameAndPassword");
                    PlayerAccount pa = new PlayerAccount(csv[1], csv[2]);
                    playerAccounts.AddLast(pa);
                }

            }
            Debug.Log("Load account complete!");
            sr.Close();
        }

    }

    // private void TTTButtonHit(int id, string[] csv)
    // {
    //     GameRoom gr = GetGameRoomWithClientID(id);
    //     string button = csv[1];

    //     int signifier = int.Parse(csv[0]);


    // }

    // private void SendMessageToClients(string msg, int id)
    // {
    //     GameRoom gr = GetGameRoomWithClientID(id);
    //     string[] csv = msg.Split(',');

    //     int signifier = int.Parse(msg);

    //     if (signifier == ClientToServerSignifiers.GGButtonPressed)
    //     {
    //         SendMessageToClient(ClientToServerSignifiers.GGButtonPressed + ",GG ButtonPressed", gr.playerID2);
    //         SendMessageToClient(ClientToServerSignifiers.GGButtonPressed + ",GG ButtonPressed", gr.playerID1);
    //     }
    //     else if(signifier == ClientToServerSignifiers.HelloButtonPressed)
    //     {
    //         SendMessageToClient(ClientToServerSignifiers.HelloButtonPressed + ",Hello ButtonPressed", gr.playerID2);
    //         SendMessageToClient(ClientToServerSignifiers.HelloButtonPressed + ",Hello ButtonPressed", gr.playerID1);
    //     }

    //     if (signifier == ServerToClientSignifiers.SendGGButtonPressed)
    //     {
    //         SendMessageToClient(ServerToClientSignifiers.SendGGButtonPressed + ",GG ButtonPressed", gr.playerID2);
    //         SendMessageToClient(ServerToClientSignifiers.SendGGButtonPressed + ",GG ButtonPressed", gr.playerID1);
    //     }
    //     else if (signifier == ServerToClientSignifiers.SendHelloButtonPressed)
    //     {
    //         SendMessageToClient(ServerToClientSignifiers.SendHelloButtonPressed + ",Hello ButtonPressed", gr.playerID2);
    //         SendMessageToClient(ServerToClientSignifiers.SendHelloButtonPressed + ",Hello ButtonPressed", gr.playerID1);
    //     }
    // }


    private GameRoom GetGameRoomWithClientID(int id)// create the game room when there are 2 players, spectator can join also
    {
        Debug.Log("game room start");
        foreach (GameRoom gr in gameRooms)
        {
            Debug.Log("Game room for each");
            if (gr.playerID1 == id || gr.playerID2 == id)
            {
                return gr;
            }
            if (gr.spectatorID == id)
            {
                Debug.Log("Spectator Joined!!!!!!!!!!");
            }

        }
        return null;
    }

    public class PlayerAccount//  set up for player account
    {

        public string name, password;

        public PlayerAccount(string Name, string Password)
        {
            name = Name;
            password = Password;
        }
    }

    public class GameRoom
    {
        public int playerID1, playerID2;
        public int spectatorID;
        public GameRoom(int PlayerID1, int PlayerID2)
        {
            playerID1 = PlayerID1;
            playerID2 = PlayerID2;





        }

    }
}
public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int JoinQueueForGameRoom = 3;
    public const int TicTacToePlay = 4;
    public const int PlayerX = 5;
    public const int PlayerO = 6;
    public const int OpponentPlay = 7;
    public const int YouWin = 8;
    public const int YouLoose = 9;
    public const int GameOver = 10;

}

public static class ServerToClientSignifiers
{
    public const int LoginComplete = 11;
    public const int LoginFailed = 12;
    public const int AccountCreationComplete = 13;
    public const int AccountCreationFailed = 14;
    public const int GameStart = 15;
    public const int OpponentPlay = 16;
    public const int PlayerX = 17;
    public const int PlayerO = 18;
    public const int YouWin = 19;
    public const int YouLoose = 20;
    public const int GameOver = 21;

}


