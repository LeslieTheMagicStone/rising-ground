using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum GameState
{
    ModeSelecting,
    Running,
}

public class GameManager : MonoBehaviour
{
    public static GameManager instance;
    public int xSize, zSize, ySize;
    [SerializeField]
    GameObject[] voxelPrefabs;
    [SerializeField]
    GameObject wallPrefab;
    [SerializeField]
    Transform spawnPoint;
    [SerializeField]
    MonsterLogic monsterPrefab;

    private float startTime = 0f;

    public bool spawnMonster = true;
    public bool spawnVoxel = true;
    const float SPAWN_MONSTER_INTERVAL_CONVERGENCE_RATE = 0.02f;
    const float MAX_SPAWN_MONSTER_INTERVAL = 3f;
    private float spawnMonsterInterval = MAX_SPAWN_MONSTER_INTERVAL;
    private float spawnMonsterTimer = MAX_SPAWN_MONSTER_INTERVAL;


    const float SPAWN_VOXEL_INTERVAL_CONVERGENCE_RATE = 0.005f;
    const float MAX_SPAWN_VOXEL_INTERVAL = 5f;
    private float spawnVoxelInterval = MAX_SPAWN_VOXEL_INTERVAL;
    private float spawnVoxelTimer = MAX_SPAWN_VOXEL_INTERVAL;

    public bool spawnUpgrade = true;
    private float spawnUpgradeInterval = 4f;
    private float spawnUpgradeTimer = 1f;
    [SerializeField] private Upgrade[] upgradePrefabs;

    private PlayerLogic player;
    private Damageable playerDam;
    [SerializeField] private DamageField crusher;
    const float CRUSHER_FALL_SPEED = 3.0f;
    public bool isGameOver = false;
    [SerializeField] private TimeLogic timeLogic;
    [SerializeField] private AudioClip[] winAudios;
    [SerializeField] private Light spotLight, mainLight;
    const float WIN_ANIM_TIME = 8.0f;
    const float LIGHT_OFF_TRANSITION_TIME = 1f;
    const float PLAYER_ELEVATE_SPEED = 5f;
    const bool USE_VOXEL_POOLING = true;
    private Queue<GameObject> voxelPool = new();
    const int VOXEL_POOL_CAPACITY = 900;

    [HideInInspector] public bool isInfiniteMode = false;
    [HideInInspector] public float time => Time.time - startTime;
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private Button normalModeButton, infiniteModeButton;
    [SerializeField] private GameObject selectModePanel;

    [HideInInspector] public GameState gameState = GameState.ModeSelecting;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else Destroy(gameObject);
    }

    private void Start()
    {
        bool hasWon = PlayerPrefs.GetString("HasWon", "false") == "true";

        if (hasWon)
        {
            Time.timeScale = 0f;
            selectModePanel.SetActive(true);

            normalModeButton.onClick.RemoveAllListeners();
            infiniteModeButton.onClick.RemoveAllListeners();

            normalModeButton.onClick.AddListener(() => SelectInfiniteMode(false));
            normalModeButton.onClick.AddListener(() => { Time.timeScale = 1f; selectModePanel.SetActive(false); gameState = GameState.Running; });
            infiniteModeButton.onClick.AddListener(() => SelectInfiniteMode(true));
            infiniteModeButton.onClick.AddListener(() => { Time.timeScale = 1f; selectModePanel.SetActive(false); gameState = GameState.Running; });
        }
        else
        {
            Time.timeScale = 1f;
            isInfiniteMode = false;
            gameState = GameState.Running;
            selectModePanel.SetActive(false);
        }

        startTime = Time.time;

        for (int x = -xSize; x <= xSize; x++)
        {
            for (int z = -zSize; z <= zSize; z++)
            {
                for (int y = 0; y < ySize; y++)
                {
                    GenerateVoxel(x, y + 1f, z);
                }
            }
        }

        var leftWall = Instantiate(wallPrefab, new Vector3(-xSize - 2.5f, 0, 0), Quaternion.identity);
        leftWall.transform.localScale = new(4f, 100f, 3 * zSize);
        var rightWall = Instantiate(wallPrefab, new Vector3(xSize + 2.5f, 0, 0), Quaternion.identity);
        rightWall.transform.localScale = new(4f, 100f, 3 * zSize);
        var forwardWall = Instantiate(wallPrefab, new Vector3(0, 0, -zSize - 2.5f), Quaternion.identity);
        forwardWall.transform.localScale = new(3 * xSize, 100f, 4f);
        var backWall = Instantiate(wallPrefab, new Vector3(0, 0, zSize + 2.5f), Quaternion.identity);
        backWall.transform.localScale = new(3 * xSize, 100f, 4f);
        var Walls = new GameObject("Walls");
        leftWall.transform.SetParent(Walls.transform);
        rightWall.transform.SetParent(Walls.transform);
        forwardWall.transform.SetParent(Walls.transform);
        backWall.transform.SetParent(Walls.transform);

        player = GameObject.FindWithTag("Player").GetComponent<PlayerLogic>();
        playerDam = player.GetComponent<Damageable>();
        playerDam.onDeath.AddListener(GameOver);
        timeLogic.onComplete.AddListener(Win);
    }

    private void Update()
    {
        if (isGameOver) return;

        UpdateUI();

        if (spawnMonster)
        {
            spawnMonsterInterval = MAX_SPAWN_MONSTER_INTERVAL * Mathf.Exp(-SPAWN_MONSTER_INTERVAL_CONVERGENCE_RATE * (Time.time - startTime));

            if (spawnMonsterTimer <= 0f)
            {
                var monster = Instantiate(monsterPrefab);
                Vector3 randomPos = GetRandomPos();
                monster.transform.position = randomPos;
                spawnMonsterTimer = spawnMonsterInterval;
            }
            spawnMonsterTimer -= Time.deltaTime;
        }

        if (spawnVoxel)
        {
            spawnVoxelInterval = MAX_SPAWN_VOXEL_INTERVAL * Mathf.Exp(-SPAWN_VOXEL_INTERVAL_CONVERGENCE_RATE * (Time.time - startTime));

            if (spawnVoxelTimer <= 0f)
            {
                for (int x = -xSize; x <= xSize; x++)
                {
                    for (int z = -zSize; z <= zSize; z++)
                    {
                        for (int y = 0; y < 1; y++)
                        {
                            GenerateVoxel(x, y + 0.1f, z);
                        }
                    }
                }
                spawnVoxelTimer = spawnVoxelInterval;
            }
            spawnVoxelTimer -= Time.deltaTime;
        }

        if (spawnUpgrade)
        {
            if (spawnUpgradeTimer <= 0f)
            {
                int randIndex = Random.Range(0, upgradePrefabs.Length);
                var upgrade = Instantiate(upgradePrefabs[randIndex]);
                Vector3 randomPos = GetRandomPos();
                upgrade.transform.position = randomPos;
                spawnUpgradeTimer = spawnUpgradeInterval;
            }
            spawnUpgradeTimer -= Time.deltaTime;
        }

        if (USE_VOXEL_POOLING)
            for (int i = 0; i < 15; i++)
                FillVoxelPool();
    }

    private void SelectInfiniteMode(bool val)
    {
        isInfiniteMode = val;
    }

    private void UpdateUI()
    {
        if (!isInfiniteMode)
        {
            timeText.SetText("Time: " + (60f - time).ToString("0.0"));
        }
        else
        {
            timeText.SetText("Time: " + time.ToString("0.0"));
        }
    }

    private void FillVoxelPool()
    {
        if (voxelPool.Count >= VOXEL_POOL_CAPACITY) return;

        GameObject voxel = GenerateVoxel(0f, 0f, 0f);
        voxel.SetActive(false);
        voxelPool.Enqueue(voxel);
    }

    public void DestroyVoxel(GameObject voxel)
    {
        if (USE_VOXEL_POOLING && voxelPool.Count < VOXEL_POOL_CAPACITY)
        {
            voxel.SetActive(false);
            voxelPool.Enqueue(voxel);
        }
        else { Destroy(voxel); };
    }

    private GameObject GenerateVoxel(float x, float y, float z)
    {
        if (!USE_VOXEL_POOLING || voxelPool.Count == 0)
        {
            // int randIndex = (int)(x + y + z) % voxelPrefabs.Length;
            // if(randIndex<0) randIndex += voxelPrefabs.Length;
            var voxelPrefab = voxelPrefabs[0];
            GameObject voxel = Instantiate(voxelPrefab);
            voxel.transform.SetParent(spawnPoint);
            voxel.transform.localPosition = new(x, y, z);
            voxel.name = "Voxel(" + x.ToString() + "," + y.ToString() + "," + z.ToString() + ")";
            return voxel;
        }
        else
        {
            GameObject voxel = voxelPool.Dequeue();
            voxel.SetActive(true);
            voxel.transform.localPosition = new(x, y, z);
            voxel.name = "Voxel(" + x.ToString() + "," + y.ToString() + "," + z.ToString() + ")";
            return voxel;
        }
    }

    private Vector3 GetRandomPos()
    {
        return new(Random.Range(-xSize + 1f, xSize - 1f), 10f, Random.Range(-zSize + 1f, zSize - 1f));
    }

    private void GameOver()
    {
        StartCoroutine(GameOverCoroutine());
    }

    private IEnumerator GameOverCoroutine()
    {
        isGameOver = true;

        crusher.canDestroyVoxel = true;
        while (crusher.transform.position.y > 0f)
        {
            crusher.transform.Translate(0, -CRUSHER_FALL_SPEED * Time.deltaTime, 0);
            yield return new WaitForEndOfFrame();
        }

        yield return new WaitForSeconds(1.5f);

        SceneReloader.instance.Reload();
    }

    private void Win()
    {
        StartCoroutine(WinCoroutine());
    }

    private IEnumerator WinCoroutine()
    {
        isGameOver = true;
        PlayerPrefs.SetString("HasWon", "true");
        float timer = 0f;
        player.SetUnmovable(10f);
        foreach (var audio in winAudios)
            AudioManager.instance.Play(audio);

        spotLight.gameObject.SetActive(true);
        spotLight.transform.position = new(player.transform.position.x, spotLight.transform.position.y, player.transform.position.z);
        float targetIntensity = spotLight.intensity;
        spotLight.intensity = 0;

        float originalIntensity = mainLight.intensity;

        crusher.GetComponent<MeshRenderer>().enabled = false;
        crusher.isFriendly = true;

        while (timer < WIN_ANIM_TIME)
        {
            float spotLightScaler = Mathf.Min(1, 4 * timer / WIN_ANIM_TIME);
            spotLight.intensity = spotLightScaler * targetIntensity;
            float mainLightScaler = Mathf.Max(0, 1 - timer / LIGHT_OFF_TRANSITION_TIME);
            mainLight.intensity = mainLightScaler * originalIntensity;
            float crusherScaler = crusher.transform.position.y > player.transform.position.y ? 3 : 1;
            crusher.transform.Translate(0, -crusherScaler * CRUSHER_FALL_SPEED * Time.deltaTime, 0);
            player.velocity = PLAYER_ELEVATE_SPEED * Vector3.up;
            timer += Time.deltaTime;
            yield return new WaitForEndOfFrame();
        }

        SceneReloader.instance.Reload();
    }
}
